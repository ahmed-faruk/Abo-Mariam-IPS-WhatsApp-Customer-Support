using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// The durable inbound acceptance step from docs/TECHNICAL.md section 14: envelope and Inbox row
/// in one transaction, both deduplicated, committed before the caller answers the provider.
/// </summary>
internal sealed class InboundMessageQueue(MessagingDbContext dbContext, MessagingTimingPolicy timing)
    : IInboundMessageQueue
{
    private const string InsertEnvelopeSql = """
        INSERT INTO messaging.webhook_envelope (envelope_hash, raw_body)
        VALUES (@envelope_hash, @raw_body::jsonb)
        ON CONFLICT (envelope_hash) DO NOTHING
        RETURNING id;
        """;

    private const string SelectEnvelopeSql = """
        SELECT id FROM messaging.webhook_envelope WHERE envelope_hash = @envelope_hash;
        """;

    private const string InsertInboxSql = """
        INSERT INTO messaging.inbox_message
            (envelope_id, provider_message_id, customer_external_id, conversation_id,
             message_type, body, provider_timestamp, partition_key)
        VALUES
            (@envelope_id, @provider_message_id, @customer_external_id, @conversation_id,
             @message_type, @body, @provider_timestamp, @partition_key)
        ON CONFLICT (provider_message_id) DO NOTHING
        RETURNING id;
        """;

    private const string SelectInboxSql = """
        SELECT id FROM messaging.inbox_message WHERE provider_message_id = @provider_message_id;
        """;

    public async Task<InboundEnqueueResult> EnqueueAsync(
        InboundMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        RequireText(envelope.RawBody, nameof(envelope));
        RequireText(envelope.ProviderMessageId, nameof(envelope));
        RequireText(envelope.CustomerExternalId, nameof(envelope));
        RequireText(envelope.MessageType, nameof(envelope));

        var envelopeHash = SHA256.HashData(Encoding.UTF8.GetBytes(envelope.RawBody));

        // The whole acceptance is one transaction: the envelope, the Inbox row, or neither.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var envelopeId = await InsertEnvelopeAsync(
                connection,
                dbTransaction,
                envelopeHash,
                envelope.RawBody,
                timing.DatabaseCommandTimeout,
                cancellationToken)
            ?? await SelectEnvelopeIdAsync(
                connection,
                dbTransaction,
                envelopeHash,
                timing.DatabaseCommandTimeout,
                cancellationToken);

        var inboxMessageId = await InsertInboxAsync(
            connection,
            dbTransaction,
            envelopeId,
            envelope,
            timing.DatabaseCommandTimeout,
            cancellationToken);

        if (inboxMessageId is null)
        {
            // A duplicate provider message id adds nothing at all, so the envelope inserted by
            // this attempt is rolled back with it and the stored Inbox message is returned.
            var storedInboxMessageId =
                await SelectInboxIdAsync(
                    connection,
                    dbTransaction,
                    envelope.ProviderMessageId,
                    timing.DatabaseCommandTimeout,
                    cancellationToken);

            await transaction.RollbackAsync(cancellationToken);

            return new InboundEnqueueResult(storedInboxMessageId, IsDuplicate: true);
        }

        await transaction.CommitAsync(cancellationToken);

        return new InboundEnqueueResult(inboxMessageId.Value, IsDuplicate: false);
    }

    private static async Task<long?> InsertEnvelopeAsync(
        DbConnection connection,
        DbTransaction transaction,
        byte[] envelopeHash,
        string rawBody,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, InsertEnvelopeSql, commandTimeout);
        MessagingQueueCommands.Add(command, "envelope_hash", envelopeHash);
        MessagingQueueCommands.Add(command, "raw_body", rawBody);

        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<long> SelectEnvelopeIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        byte[] envelopeHash,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, SelectEnvelopeSql, commandTimeout);
        MessagingQueueCommands.Add(command, "envelope_hash", envelopeHash);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull
            ? throw new InvalidOperationException("The webhook envelope disappeared during the transaction.")
            : (long)value;
    }

    private static async Task<long?> InsertInboxAsync(
        DbConnection connection,
        DbTransaction transaction,
        long envelopeId,
        InboundMessageEnvelope envelope,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, InsertInboxSql, commandTimeout);
        MessagingQueueCommands.Add(command, "envelope_id", envelopeId);
        MessagingQueueCommands.Add(command, "provider_message_id", envelope.ProviderMessageId);
        MessagingQueueCommands.Add(command, "customer_external_id", envelope.CustomerExternalId);
        MessagingQueueCommands.Add(command, "conversation_id", envelope.ConversationId);
        MessagingQueueCommands.Add(command, "message_type", envelope.MessageType);
        MessagingQueueCommands.Add(command, "body", envelope.Body);
        MessagingQueueCommands.Add(command, "provider_timestamp", MessagingQueueCommands.ToUtc(envelope.ProviderTimestamp));
        // Inbound work is ordered per conversation. The conversation may not exist yet, and the
        // documented model keeps one open conversation per customer, so the customer is the partition.
        MessagingQueueCommands.Add(command, "partition_key", envelope.CustomerExternalId);

        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<long> SelectInboxIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        string providerMessageId,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = MessagingQueueCommands.Create(connection, transaction, SelectInboxSql, commandTimeout);
        MessagingQueueCommands.Add(command, "provider_message_id", providerMessageId);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null or DBNull
            ? throw new InvalidOperationException("The Inbox message disappeared during the transaction.")
            : (long)value;
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value must not be empty.", parameterName);
        }
    }
}
