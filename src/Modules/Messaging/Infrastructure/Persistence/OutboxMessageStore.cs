using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The durable Outbox queue from docs/TECHNICAL.md section 15. Claiming, retrying and
/// dead-lettering never touch the stored body or its hash. Claims are leased and serialized per
/// partition with a transaction-scoped PostgreSQL advisory lock, so two concurrent claim
/// transactions can never take two messages of one partition, and a claim whose owner disappeared
/// becomes claimable again once its lease expires.
/// </summary>
internal sealed class OutboxMessageStore(
    MessagingDbContext dbContext,
    MessagingQueueOptions options,
    MessagingTimingPolicy timing) : IOutboxMessageStore
{
    /// <summary>A Failed message whose retry is due re-enters Pending, keeping the claim predicate documented.</summary>
    private const string RequeueDueRetriesSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = 'Pending'
        WHERE delivery_status = 'Failed' AND run_after <= now();
        """;

    /// <summary>
    /// Work abandoned by a worker that crashed, was cancelled or lost its database connection. The
    /// lease is PostgreSQL-owned: only the clock releases it, and the recovery is bounded exactly
    /// like a claim, so one poll never rewrites an unbounded number of rows.
    /// A recovered attempt is uncertain either way: the abandoned owner may already have left the
    /// transport, so the recovery is recorded as an unknown provider outcome, never as a refusal.
    /// </summary>
    private const string RecoverExpiredClaimsSql = """
        UPDATE messaging.outbox_message AS m
        SET delivery_status = CASE
                WHEN m.attempts >= m.max_attempts THEN 'DeadLettered'
                ELSE 'Pending'
            END,
            last_error = CASE
                WHEN m.attempts >= m.max_attempts THEN @exhausted_error
                ELSE @recovered_error
            END,
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE m.id IN (
            SELECT expired.id
            FROM messaging.outbox_message AS expired
            WHERE expired.delivery_status = 'Claimed'
              AND expired.claim_expires_at <= now()
            ORDER BY expired.id
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size)
        RETURNING m.id;
        """;

    /// <summary>
    /// The claim statement itself. It is module-internal rather than private so the persistence
    /// suite can hold the real claim of this store open in a transaction while a second claimer runs.
    /// </summary>
    internal const string ClaimSql = """
        WITH eligible AS MATERIALIZED (
            -- At most one claimable message per partition, collapsed before the batch limit is
            -- applied, so a hot partition can never spend the batch budget on rows that the
            -- per-partition rule would discard afterwards.
            SELECT DISTINCT ON (m.partition_key) m.id, m.partition_key
            FROM messaging.outbox_message AS m
            WHERE m.delivery_status = 'Pending'
              AND m.run_after <= now()
            ORDER BY m.partition_key, m.id
        ),
        candidate AS MATERIALIZED (
            SELECT m.id, m.partition_key
            FROM messaging.outbox_message AS m
            WHERE m.id IN (SELECT eligible.id FROM eligible)
              AND NOT EXISTS (
                  -- A later reply of a partition never overtakes an earlier nonterminal one:
                  -- Pending, Claimed and Failed all own the order, even while an earlier retry is
                  -- still scheduled in the future.
                  SELECT 1
                  FROM messaging.outbox_message AS ahead
                  WHERE ahead.partition_key = m.partition_key
                    AND ahead.id < m.id
                    AND ahead.delivery_status IN ('Pending','Claimed','Failed'))
            ORDER BY m.id
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size
        ),
        locked AS MATERIALIZED (
            -- Two claim transactions must never take two messages of one partition. A claim that is
            -- still uncommitted is invisible to the NOT EXISTS above, and SKIP LOCKED only steps
            -- over the row that holds it, so the partition itself is locked for the rest of this
            -- transaction. A claimer that loses the lock steps over the whole partition.
            SELECT candidate.id, candidate.partition_key
            FROM candidate
            WHERE pg_try_advisory_xact_lock(hashtextextended(candidate.partition_key, 0))
        ),
        selected AS (
            SELECT DISTINCT ON (locked.partition_key) locked.id
            FROM locked
            ORDER BY locked.partition_key, locked.id
        )
        UPDATE messaging.outbox_message AS m
        SET delivery_status = 'Claimed',
            claimed_at = now(),
            claim_token = @claim_token,
            claim_expires_at = now() + @claim_lease,
            attempts = m.attempts + 1
        FROM selected
        WHERE m.id = selected.id
          AND m.delivery_status = 'Pending'
          AND m.run_after <= now()
          AND NOT EXISTS (
              -- The partition lock is taken after the candidate rows are read, so a concurrent
              -- claimer can hold an earlier message of the partition without having committed it.
              -- The guard keeps the documented oldest-first order of a partition intact even then.
              SELECT 1
              FROM messaging.outbox_message AS ahead
              WHERE ahead.partition_key = m.partition_key
                AND ahead.id < m.id
                AND ahead.delivery_status IN ('Pending','Claimed','Failed'))
        RETURNING m.id, m.conversation_id, m.customer_external_id, m.correlation_id, m.sender,
                  m.body, m.provider_message_id, m.attempts, m.max_attempts;
        """;

    private const string CompleteSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = 'Sent',
            sent_at = now(),
            provider_message_id = @provider_message_id,
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE id = @outbox_message_id
          AND delivery_status = 'Claimed'
          AND claim_token = @claim_token
        RETURNING id;
        """;

    private const string CompletedDeliverySql = """
        SELECT delivery_status, provider_message_id
        FROM messaging.outbox_message
        WHERE id = @message_id;
        """;

    private const string FailSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = CASE WHEN @terminal OR attempts >= max_attempts THEN 'DeadLettered' ELSE 'Failed' END,
            last_error = @last_error,
            run_after = CASE WHEN @terminal OR attempts >= max_attempts THEN run_after ELSE now() + @retry_delay END,
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE id = @outbox_message_id
          AND delivery_status = 'Claimed'
          AND claim_token = @claim_token
        RETURNING delivery_status;
        """;

    private const string StatusSql = """
        SELECT delivery_status FROM messaging.outbox_message WHERE id = @message_id;
        """;

    /// <summary>
    /// The stable delivery key of one logical Outbox message. It is derived from the durable Outbox
    /// identity, so a retry and a later reclaim of the same message always present the same key to
    /// the transport for local logs and correlation. It is not a provider idempotency guarantee.
    /// </summary>
    private static string DeliveryKey(long outboxMessageId) =>
        string.Create(CultureInfo.InvariantCulture, $"outbox:{outboxMessageId}");

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "The batch size must be positive.");
        }

        // One claim is one lease: every message of this batch belongs to this token, and an owner
        // that lost its lease can never record an outcome for a newer claim of the same message.
        var claimToken = Guid.NewGuid();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        await ExecuteAsync(connection, dbTransaction, RequeueDueRetriesSql, timing.DatabaseCommandTimeout, cancellationToken);
        await RecoverExpiredClaimsAsync(connection, dbTransaction, batchSize, cancellationToken);

        var claimed = new List<ClaimedOutboxMessage>();

        await using (var command = MessagingQueueCommands.Create(
            connection,
            dbTransaction,
            ClaimSql,
            timing.DatabaseCommandTimeout))
        {
            MessagingQueueCommands.Add(command, "batch_size", batchSize);
            MessagingQueueCommands.Add(command, "claim_token", claimToken);
            MessagingQueueCommands.Add(command, "claim_lease", options.ClaimLeaseDuration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);

                claimed.Add(new ClaimedOutboxMessage(
                    Id: id,
                    ConversationId: reader.GetInt64(1),
                    CustomerExternalId: reader.GetString(2),
                    CorrelationId: reader.GetString(3),
                    Sender: reader.GetString(4),
                    Body: reader.GetString(5),
                    ProviderMessageId: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Attempts: reader.GetInt32(7),
                    MaxAttempts: reader.GetInt32(8),
                    DeliveryKey: DeliveryKey(id),
                    ClaimToken: claimToken));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return [.. claimed.OrderBy(message => message.Id)];
    }

    public async Task CompleteAsync(
        long outboxMessageId,
        Guid claimToken,
        string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);

        await using (var command = MessagingQueueCommands.Create(
            connection,
            null,
            CompleteSql,
            timing.DatabaseCommandTimeout))
        {
            MessagingQueueCommands.Add(command, "outbox_message_id", outboxMessageId);
            MessagingQueueCommands.Add(command, "claim_token", claimToken);
            MessagingQueueCommands.Add(command, "provider_message_id", providerMessageId);

            if (await command.ExecuteScalarAsync(cancellationToken) is not null and not DBNull)
            {
                return;
            }
        }

        // The update matched nothing, so either this accepted delivery is already recorded - the
        // same successful provider result, which makes the repeat a no-op - or the lease expired and
        // another owner holds the claim, which this stale owner must not overwrite.
        await using var read = MessagingQueueCommands.Create(
            connection,
            null,
            CompletedDeliverySql,
            timing.DatabaseCommandTimeout);
        MessagingQueueCommands.Add(read, "message_id", outboxMessageId);

        await using var reader = await read.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"The Outbox message {outboxMessageId} does not exist.");
        }

        var deliveryStatus = reader.GetString(0);
        var storedProviderMessageId = reader.IsDBNull(1) ? null : reader.GetString(1);

        if (string.Equals(deliveryStatus, OutboxDeliveryStatuses.Sent, StringComparison.Ordinal)
            && string.Equals(storedProviderMessageId, providerMessageId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ClaimOwnershipLostException(
            $"The Outbox message {outboxMessageId} is {deliveryStatus} and the presented claim token does "
            + "not own it, so the accepted delivery was not recorded.");
    }

    public async Task<QueueFailureOutcome> FailAsync(
        long outboxMessageId,
        Guid claimToken,
        string error,
        bool terminal = false,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        // The durable schedule is bounded here, not only in the transport: a provider hint that asks
        // for a far-away retry is clamped to the queue policy, so no single hint can park the oldest
        // message of a partition decades into the future.
        var delay = retryDelay ?? options.OutboxRetryDelay;

        if (delay > options.OutboxMaxRetryDelay)
        {
            delay = options.OutboxMaxRetryDelay;
        }

        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken),
            null,
            FailSql,
            timing.DatabaseCommandTimeout);
        MessagingQueueCommands.Add(command, "outbox_message_id", outboxMessageId);
        MessagingQueueCommands.Add(command, "claim_token", claimToken);
        MessagingQueueCommands.Add(command, "last_error", error);
        MessagingQueueCommands.Add(command, "terminal", terminal);
        MessagingQueueCommands.Add(command, "retry_delay", delay);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw await MessagingQueueCommands.LostClaimAsync(
                command.Connection!,
                StatusSql,
                "Outbox message",
                outboxMessageId,
                timing.DatabaseCommandTimeout,
                cancellationToken);

        return string.Equals(status, OutboxDeliveryStatuses.DeadLettered, StringComparison.Ordinal)
            ? QueueFailureOutcome.DeadLettered
            : QueueFailureOutcome.RetryScheduled;
    }

    private async Task RecoverExpiredClaimsAsync(
        DbConnection connection,
        DbTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(
            connection,
            transaction,
            RecoverExpiredClaimsSql,
            timing.DatabaseCommandTimeout);
        MessagingQueueCommands.Add(command, "batch_size", batchSize);
        MessagingQueueCommands.Add(command, "recovered_error", MessagingDiagnostics.ExpiredClaimRecovered);
        MessagingQueueCommands.Add(command, "exhausted_error", MessagingDiagnostics.ExpiredClaimExhausted);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, sql, commandTimeout);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
