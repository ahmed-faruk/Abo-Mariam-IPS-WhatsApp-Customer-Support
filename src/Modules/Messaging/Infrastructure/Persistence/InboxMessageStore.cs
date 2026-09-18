using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The durable Inbox queue from docs/TECHNICAL.md section 15: exclusive claiming with
/// <c>FOR UPDATE SKIP LOCKED</c>, one message per partition, oldest message first. Claims are leased
/// and serialized per partition with a transaction-scoped PostgreSQL advisory lock, so two
/// concurrent claim transactions can never take two messages of one partition, and a claim whose
/// owner disappeared becomes claimable again once its lease expires.
/// </summary>
internal sealed class InboxMessageStore(MessagingDbContext dbContext, MessagingQueueOptions options)
    : IInboxMessageStore
{
    /// <summary>
    /// A Failed message whose retry is due re-enters Pending, so the documented claim predicate
    /// stays exactly <c>processing_status = 'Pending'</c> and the partial claim index stays usable.
    /// </summary>
    private const string RequeueDueRetriesSql = """
        UPDATE messaging.inbox_message
        SET processing_status = 'Pending'
        WHERE processing_status = 'Failed' AND run_after <= now();
        """;

    /// <summary>
    /// Work abandoned by a worker that crashed, was cancelled or lost its database connection. The
    /// lease is PostgreSQL-owned: only the clock releases it, and the recovery is bounded exactly
    /// like a claim, so one poll never rewrites an unbounded number of rows.
    /// </summary>
    private const string RecoverExpiredClaimsSql = """
        UPDATE messaging.inbox_message AS i
        SET processing_status = 'Pending',
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE i.id IN (
            SELECT expired.id
            FROM messaging.inbox_message AS expired
            WHERE expired.processing_status = 'Claimed'
              AND expired.claim_expires_at <= now()
            ORDER BY expired.id
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size)
        RETURNING i.id;
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
            SELECT DISTINCT ON (i.partition_key) i.id, i.partition_key
            FROM messaging.inbox_message AS i
            WHERE i.processing_status = 'Pending'
              AND i.run_after <= now()
            ORDER BY i.partition_key, i.id
        ),
        candidate AS MATERIALIZED (
            SELECT i.id, i.partition_key
            FROM messaging.inbox_message AS i
            WHERE i.id IN (SELECT eligible.id FROM eligible)
              AND NOT EXISTS (
                  -- A later message of a partition never overtakes an earlier nonterminal one:
                  -- Pending, Claimed and Failed all own the order, even while an earlier retry is
                  -- still scheduled in the future.
                  SELECT 1
                  FROM messaging.inbox_message AS ahead
                  WHERE ahead.partition_key = i.partition_key
                    AND ahead.id < i.id
                    AND ahead.processing_status IN ('Pending','Claimed','Failed'))
            ORDER BY i.id
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
        UPDATE messaging.inbox_message AS i
        SET processing_status = 'Claimed',
            claimed_at = now(),
            claim_token = @claim_token,
            claim_expires_at = now() + @claim_lease,
            attempts = i.attempts + 1
        FROM selected
        WHERE i.id = selected.id
          AND i.processing_status = 'Pending'
          AND i.run_after <= now()
          AND NOT EXISTS (
              -- The partition lock is taken after the candidate rows are read, so a concurrent
              -- claimer can hold an earlier message of the partition without having committed it.
              -- The guard keeps the documented oldest-first order of a partition intact even then.
              SELECT 1
              FROM messaging.inbox_message AS ahead
              WHERE ahead.partition_key = i.partition_key
                AND ahead.id < i.id
                AND ahead.processing_status IN ('Pending','Claimed','Failed'))
        RETURNING i.id, i.provider_message_id, i.customer_external_id, i.conversation_id,
                  i.message_type, i.body, i.provider_timestamp, i.attempts;
        """;

    private const string CompleteSql = """
        UPDATE messaging.inbox_message
        SET processing_status = 'Processed',
            processed_at = now(),
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE id = @inbox_message_id
          AND processing_status = 'Claimed'
          AND claim_token = @claim_token
        RETURNING id;
        """;

    private const string FailSql = """
        UPDATE messaging.inbox_message
        SET processing_status = CASE WHEN attempts >= @max_attempts THEN 'DeadLettered' ELSE 'Failed' END,
            last_error = @last_error,
            run_after = CASE WHEN attempts >= @max_attempts THEN run_after ELSE now() + @retry_delay END,
            claim_token = NULL,
            claim_expires_at = NULL
        WHERE id = @inbox_message_id
          AND processing_status = 'Claimed'
          AND claim_token = @claim_token
        RETURNING processing_status;
        """;

    private const string StatusSql = """
        SELECT processing_status FROM messaging.inbox_message WHERE id = @message_id;
        """;

    public async Task<IReadOnlyList<ClaimedInboxMessage>> ClaimAsync(
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

        await ExecuteAsync(connection, dbTransaction, RequeueDueRetriesSql, cancellationToken);
        await RecoverExpiredClaimsAsync(connection, dbTransaction, batchSize, cancellationToken);

        var claimed = new List<ClaimedInboxMessage>();

        await using (var command = MessagingQueueCommands.Create(connection, dbTransaction, ClaimSql))
        {
            MessagingQueueCommands.Add(command, "batch_size", batchSize);
            MessagingQueueCommands.Add(command, "claim_token", claimToken);
            MessagingQueueCommands.Add(command, "claim_lease", options.ClaimLeaseDuration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedInboxMessage(
                    Id: reader.GetInt64(0),
                    ProviderMessageId: reader.GetString(1),
                    CustomerExternalId: reader.GetString(2),
                    ConversationId: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    MessageType: reader.GetString(4),
                    Body: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ProviderTimestamp: reader.GetDateTime(6),
                    Attempts: reader.GetInt32(7),
                    ClaimToken: claimToken));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // RETURNING has no guaranteed order, so the worker still gets the documented id order.
        return [.. claimed.OrderBy(message => message.Id)];
    }

    public async Task CompleteAsync(
        long inboxMessageId,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, CompleteSql);
        MessagingQueueCommands.Add(command, "inbox_message_id", inboxMessageId);
        MessagingQueueCommands.Add(command, "claim_token", claimToken);

        if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull)
        {
            throw await MessagingQueueCommands.LostClaimAsync(
                command.Connection!,
                StatusSql,
                "Inbox message",
                inboxMessageId,
                cancellationToken);
        }
    }

    public async Task<QueueFailureOutcome> FailAsync(
        long inboxMessageId,
        Guid claimToken,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, FailSql);
        MessagingQueueCommands.Add(command, "inbox_message_id", inboxMessageId);
        MessagingQueueCommands.Add(command, "claim_token", claimToken);
        MessagingQueueCommands.Add(command, "last_error", error);
        MessagingQueueCommands.Add(command, "max_attempts", options.InboxMaxAttempts);
        MessagingQueueCommands.Add(command, "retry_delay", options.InboxRetryDelay);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw await MessagingQueueCommands.LostClaimAsync(
                command.Connection!,
                StatusSql,
                "Inbox message",
                inboxMessageId,
                cancellationToken);

        return string.Equals(status, InboxProcessingStatuses.DeadLettered, StringComparison.Ordinal)
            ? QueueFailureOutcome.DeadLettered
            : QueueFailureOutcome.RetryScheduled;
    }

    private static async Task RecoverExpiredClaimsAsync(
        DbConnection connection,
        DbTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, RecoverExpiredClaimsSql);
        MessagingQueueCommands.Add(command, "batch_size", batchSize);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, sql);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
