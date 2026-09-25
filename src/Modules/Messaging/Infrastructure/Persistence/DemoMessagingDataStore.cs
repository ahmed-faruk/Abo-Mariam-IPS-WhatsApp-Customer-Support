using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The controlled-demo Messaging reset. The rows of the named customers are locked before their
/// statuses are checked, and only the locked rows are deleted, so work that arrives during the reset
/// is left alone and work that is still open makes the whole reset refuse. A webhook envelope is only
/// removed once no Inbox row references it, because the Inbox foreign key cascades from the envelope
/// and an envelope can carry messages of other customers.
/// </summary>
internal sealed class DemoMessagingDataStore(
    MessagingDbContext dbContext,
    MessagingTimingPolicy timingPolicy) : IDemoMessagingData
{
    private const string CountSql = """
        SELECT
            (SELECT count(*)::int FROM messaging.inbox_message
             WHERE customer_external_id = ANY(@customer_ids)),
            (SELECT count(*)::int FROM messaging.outbox_message
             WHERE customer_external_id = ANY(@customer_ids)),
            (SELECT count(*)::int FROM messaging.inbox_message
             WHERE customer_external_id = ANY(@customer_ids) AND processing_status = ANY(@open_inbox))
            + (SELECT count(*)::int FROM messaging.outbox_message
               WHERE customer_external_id = ANY(@customer_ids) AND delivery_status = ANY(@open_outbox));
        """;

    private const string LockInboxSql = """
        SELECT id, envelope_id, processing_status
        FROM messaging.inbox_message
        WHERE customer_external_id = ANY(@customer_ids)
        ORDER BY id
        FOR UPDATE;
        """;

    private const string LockOutboxSql = """
        SELECT id, delivery_status
        FROM messaging.outbox_message
        WHERE customer_external_id = ANY(@customer_ids)
        ORDER BY id
        FOR UPDATE;
        """;

    private const string LockEnvelopesSql = """
        SELECT id
        FROM messaging.webhook_envelope
        WHERE id = ANY(@envelope_ids)
        ORDER BY id
        FOR UPDATE;
        """;

    private const string DeleteOutboxSql = """
        DELETE FROM messaging.outbox_message
        WHERE id = ANY(@outbox_ids);
        """;

    private const string DeleteInboxSql = """
        DELETE FROM messaging.inbox_message
        WHERE id = ANY(@inbox_ids);
        """;

    private const string DeleteOrphanedEnvelopesSql = """
        DELETE FROM messaging.webhook_envelope e
        WHERE e.id = ANY(@envelope_ids)
          AND NOT EXISTS (
              SELECT 1 FROM messaging.inbox_message i WHERE i.envelope_id = e.id);
        """;

    private static readonly string[] OpenInboxStatuses =
    [
        InboxProcessingStatuses.Pending,
        InboxProcessingStatuses.Claimed,
        InboxProcessingStatuses.Failed,
    ];

    private static readonly string[] OpenOutboxStatuses =
    [
        OutboxDeliveryStatuses.Pending,
        OutboxDeliveryStatuses.Claimed,
        OutboxDeliveryStatuses.Failed,
    ];

    public async Task<DemoMessagingCounts> CountAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default)
    {
        var ids = RequireCustomerIds(customerExternalIds);
        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);

        await using var command = Create(connection, null, CountSql);
        MessagingQueueCommands.Add(command, "customer_ids", ids);
        MessagingQueueCommands.Add(command, "open_inbox", OpenInboxStatuses);
        MessagingQueueCommands.Add(command, "open_outbox", OpenOutboxStatuses);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new DemoMessagingCounts(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    public async Task<DemoMessagingDeleteResult> DeleteAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default)
    {
        var ids = RequireCustomerIds(customerExternalIds);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var inboxIds = new List<long>();
        var envelopeIds = new HashSet<long>();
        var open = false;

        await using (var command = Create(connection, dbTransaction, LockInboxSql))
        {
            MessagingQueueCommands.Add(command, "customer_ids", ids);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                inboxIds.Add(reader.GetInt64(0));
                envelopeIds.Add(reader.GetInt64(1));
                open |= OpenInboxStatuses.Contains(reader.GetString(2), StringComparer.Ordinal);
            }
        }

        var outboxIds = new List<long>();

        await using (var command = Create(connection, dbTransaction, LockOutboxSql))
        {
            MessagingQueueCommands.Add(command, "customer_ids", ids);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                outboxIds.Add(reader.GetInt64(0));
                open |= OpenOutboxStatuses.Contains(reader.GetString(1), StringComparer.Ordinal);
            }
        }

        if (open)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new DemoMessagingDeleteResult(Refused: true, Inbox: 0, Outbox: 0, Envelopes: 0);
        }

        var candidateEnvelopes = envelopeIds.ToArray();

        await ExecuteAsync(connection, dbTransaction, LockEnvelopesSql, "envelope_ids", candidateEnvelopes, cancellationToken);
        var deletedOutbox = await ExecuteAsync(
            connection, dbTransaction, DeleteOutboxSql, "outbox_ids", outboxIds.ToArray(), cancellationToken);
        var deletedInbox = await ExecuteAsync(
            connection, dbTransaction, DeleteInboxSql, "inbox_ids", inboxIds.ToArray(), cancellationToken);
        var deletedEnvelopes = await ExecuteAsync(
            connection, dbTransaction, DeleteOrphanedEnvelopesSql, "envelope_ids", candidateEnvelopes, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DemoMessagingDeleteResult(
            Refused: false,
            Inbox: deletedInbox,
            Outbox: deletedOutbox,
            Envelopes: deletedEnvelopes);
    }

    private DbCommand Create(DbConnection connection, DbTransaction? transaction, string sql) =>
        MessagingQueueCommands.Create(connection, transaction, sql, timingPolicy.DatabaseCommandTimeout);

    private async Task<int> ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        string parameterName,
        long[] values,
        CancellationToken cancellationToken)
    {
        await using var command = Create(connection, transaction, sql);
        MessagingQueueCommands.Add(command, parameterName, values);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>At least one customer id and no blank one, so a reset can never target everybody.</summary>
    private static string[] RequireCustomerIds(IReadOnlyList<string> customerExternalIds)
    {
        ArgumentNullException.ThrowIfNull(customerExternalIds);

        if (customerExternalIds.Count == 0)
        {
            throw new ArgumentException("At least one customer id is required.", nameof(customerExternalIds));
        }

        foreach (var id in customerExternalIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(customerExternalIds));
        }

        return [.. customerExternalIds.Distinct(StringComparer.Ordinal)];
    }
}
