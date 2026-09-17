using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The durable Inbox queue from docs/TECHNICAL.md section 15: exclusive claiming with
/// <c>FOR UPDATE SKIP LOCKED</c>, one message per partition, oldest id first.
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

    private const string ClaimSql = """
        WITH candidate AS MATERIALIZED (
            SELECT i.id, i.partition_key
            FROM messaging.inbox_message AS i
            WHERE i.processing_status = 'Pending'
              AND i.run_after <= now()
              AND NOT EXISTS (
                  SELECT 1
                  FROM messaging.inbox_message AS claimed
                  WHERE claimed.partition_key = i.partition_key
                    AND claimed.processing_status = 'Claimed')
            ORDER BY i.id
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size
        ),
        selected AS (
            SELECT DISTINCT ON (candidate.partition_key) candidate.id
            FROM candidate
            ORDER BY candidate.partition_key, candidate.id
        )
        UPDATE messaging.inbox_message AS i
        SET processing_status = 'Claimed',
            claimed_at = now(),
            attempts = i.attempts + 1
        FROM selected
        WHERE i.id = selected.id
        RETURNING i.id, i.provider_message_id, i.customer_external_id, i.conversation_id,
                  i.message_type, i.body, i.provider_timestamp, i.attempts;
        """;

    private const string CompleteSql = """
        UPDATE messaging.inbox_message
        SET processing_status = 'Processed',
            processed_at = now()
        WHERE id = @inbox_message_id
        RETURNING id;
        """;

    private const string FailSql = """
        UPDATE messaging.inbox_message
        SET processing_status = CASE WHEN attempts >= @max_attempts THEN 'DeadLettered' ELSE 'Failed' END,
            last_error = @last_error,
            run_after = CASE WHEN attempts >= @max_attempts THEN run_after ELSE now() + @retry_delay END
        WHERE id = @inbox_message_id
        RETURNING processing_status;
        """;

    public async Task<IReadOnlyList<ClaimedInboxMessage>> ClaimAsync(
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

        var claimed = new List<ClaimedInboxMessage>();

        await using (var command = MessagingQueueCommands.Create(connection, dbTransaction, ClaimSql))
        {
            MessagingQueueCommands.Add(command, "batch_size", batchSize);

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
                    Attempts: reader.GetInt32(7)));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // RETURNING has no guaranteed order, so the worker still gets the documented id order.
        return [.. claimed.OrderBy(message => message.Id)];
    }

    public async Task CompleteAsync(long inboxMessageId, CancellationToken cancellationToken = default)
    {
        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, CompleteSql);
        MessagingQueueCommands.Add(command, "inbox_message_id", inboxMessageId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null or DBNull)
        {
            throw new InvalidOperationException($"Inbox message {inboxMessageId} does not exist.");
        }
    }

    public async Task<QueueFailureOutcome> FailAsync(
        long inboxMessageId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, FailSql);
        MessagingQueueCommands.Add(command, "inbox_message_id", inboxMessageId);
        MessagingQueueCommands.Add(command, "last_error", error);
        MessagingQueueCommands.Add(command, "max_attempts", options.InboxMaxAttempts);
        MessagingQueueCommands.Add(command, "retry_delay", options.InboxRetryDelay);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException($"Inbox message {inboxMessageId} does not exist.");

        return string.Equals(status, InboxProcessingStatuses.DeadLettered, StringComparison.Ordinal)
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
