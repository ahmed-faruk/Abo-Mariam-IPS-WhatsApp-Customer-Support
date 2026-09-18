using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The durable Outbox queue from docs/TECHNICAL.md section 15. Claiming, retrying and
/// dead-lettering never touch the stored body or its hash. Claims are serialized per partition with
/// a transaction-scoped PostgreSQL advisory lock, so two concurrent claim transactions can never
/// take two messages of one partition.
/// </summary>
internal sealed class OutboxMessageStore(MessagingDbContext dbContext, MessagingQueueOptions options)
    : IOutboxMessageStore
{
    /// <summary>A Failed message whose retry is due re-enters Pending, keeping the claim predicate documented.</summary>
    private const string RequeueDueRetriesSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = 'Pending'
        WHERE delivery_status = 'Failed' AND run_after <= now();
        """;

    private const string ClaimSql = """
        WITH candidate AS MATERIALIZED (
            SELECT m.id, m.partition_key
            FROM messaging.outbox_message AS m
            WHERE m.delivery_status = 'Pending'
              AND m.run_after <= now()
              AND NOT EXISTS (
                  SELECT 1
                  FROM messaging.outbox_message AS claimed
                  WHERE claimed.partition_key = m.partition_key
                    AND claimed.delivery_status = 'Claimed')
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
            attempts = m.attempts + 1
        FROM selected
        WHERE m.id = selected.id
          AND NOT EXISTS (
              -- The partition lock is taken after the candidate rows are read, so a concurrent
              -- claimer can hold an earlier message of the partition without having committed it.
              -- A later message is therefore never taken while an earlier one is still claimable,
              -- so the documented oldest-id-first order of a partition is never broken.
              SELECT 1
              FROM messaging.outbox_message AS ahead
              WHERE ahead.partition_key = m.partition_key
                AND ahead.id < m.id
                AND ahead.delivery_status = 'Pending'
                AND ahead.run_after <= now())
        RETURNING m.id, m.conversation_id, m.customer_external_id, m.correlation_id, m.sender,
                  m.body, m.provider_message_id, m.attempts, m.max_attempts;
        """;

    private const string CompleteSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = 'Sent',
            sent_at = now(),
            provider_message_id = @provider_message_id
        WHERE id = @outbox_message_id
        RETURNING id;
        """;

    private const string FailSql = """
        UPDATE messaging.outbox_message
        SET delivery_status = CASE WHEN attempts >= max_attempts THEN 'DeadLettered' ELSE 'Failed' END,
            last_error = @last_error,
            run_after = CASE WHEN attempts >= max_attempts THEN run_after ELSE now() + @retry_delay END
        WHERE id = @outbox_message_id
        RETURNING delivery_status;
        """;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "The batch size must be positive.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        await ExecuteAsync(connection, dbTransaction, RequeueDueRetriesSql, cancellationToken);

        var claimed = new List<ClaimedOutboxMessage>();

        await using (var command = MessagingQueueCommands.Create(connection, dbTransaction, ClaimSql))
        {
            MessagingQueueCommands.Add(command, "batch_size", batchSize);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedOutboxMessage(
                    Id: reader.GetInt64(0),
                    ConversationId: reader.GetInt64(1),
                    CustomerExternalId: reader.GetString(2),
                    CorrelationId: reader.GetString(3),
                    Sender: reader.GetString(4),
                    Body: reader.GetString(5),
                    ProviderMessageId: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Attempts: reader.GetInt32(7),
                    MaxAttempts: reader.GetInt32(8)));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return [.. claimed.OrderBy(message => message.Id)];
    }

    public async Task CompleteAsync(
        long outboxMessageId,
        string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, CompleteSql);
        MessagingQueueCommands.Add(command, "outbox_message_id", outboxMessageId);
        MessagingQueueCommands.Add(command, "provider_message_id", providerMessageId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull)
        {
            throw new InvalidOperationException($"Outbox message {outboxMessageId} does not exist.");
        }
    }

    public async Task<QueueFailureOutcome> FailAsync(
        long outboxMessageId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, FailSql);
        MessagingQueueCommands.Add(command, "outbox_message_id", outboxMessageId);
        MessagingQueueCommands.Add(command, "last_error", error);
        MessagingQueueCommands.Add(command, "retry_delay", options.OutboxRetryDelay);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException($"Outbox message {outboxMessageId} does not exist.");

        return string.Equals(status, OutboxDeliveryStatuses.DeadLettered, StringComparison.Ordinal)
            ? QueueFailureOutcome.DeadLettered
            : QueueFailureOutcome.RetryScheduled;
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
