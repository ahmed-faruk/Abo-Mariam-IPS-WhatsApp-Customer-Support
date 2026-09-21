using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// Durable outbound acceptance: the intent is stored, with its content hash, before any transport
/// send can be attempted. The stored body is never updated afterwards.
/// </summary>
/// <remarks>
/// Enqueueing is idempotent per correlation id. The correlation is the inbound provider message id, so
/// a turn that is processed twice because its Inbox message was retried reuses the reply it already
/// stored instead of creating a second durable message.
/// </remarks>
internal sealed class OutboundMessageQueue(MessagingDbContext dbContext) : IOutboundMessageQueue
{
    private const string InsertOutboxSql = """
        INSERT INTO messaging.outbox_message
            (conversation_id, customer_external_id, correlation_id, sender, body, body_hash, partition_key)
        VALUES
            (@conversation_id, @customer_external_id, @correlation_id, @sender, @body, @body_hash, @partition_key)
        ON CONFLICT (correlation_id) DO NOTHING
        RETURNING id;
        """;

    private const string SelectByCorrelationSql = """
        SELECT id FROM messaging.outbox_message WHERE correlation_id = @correlation_id;
        """;

    public async Task<long> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.CustomerExternalId, nameof(request));
        RequireText(request.CorrelationId, nameof(request));
        RequireText(request.Body, nameof(request));
        RequireSender(request.Sender, nameof(request));

        if (request.ConversationId <= 0)
        {
            throw new ArgumentException("The conversation id must be positive.", nameof(request));
        }

        // A single INSERT is the whole transaction: once it returns, the reply survives a crash. The
        // insert is left to PostgreSQL's own conflict handling, so two concurrent turns that share one
        // correlation cannot both create a row.
        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, InsertOutboxSql);
        MessagingQueueCommands.Add(command, "conversation_id", request.ConversationId);
        MessagingQueueCommands.Add(command, "customer_external_id", request.CustomerExternalId);
        MessagingQueueCommands.Add(command, "correlation_id", request.CorrelationId);
        MessagingQueueCommands.Add(command, "sender", request.Sender);
        MessagingQueueCommands.Add(command, "body", request.Body);
        MessagingQueueCommands.Add(command, "body_hash", SHA256.HashData(Encoding.UTF8.GetBytes(request.Body)));
        MessagingQueueCommands.Add(command, "partition_key", request.ConversationId.ToString(CultureInfo.InvariantCulture));

        if (await command.ExecuteScalarAsync(cancellationToken) is long insertedId)
        {
            return insertedId;
        }

        // The conflict was with a committed row, either from an earlier attempt of this same turn or
        // from a concurrent worker. Reusing that row is the whole point of the correlation: the reply
        // is already durable, so this turn must not create another one.
        await using var existing = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, SelectByCorrelationSql);
        MessagingQueueCommands.Add(existing, "correlation_id", request.CorrelationId);

        return await existing.ExecuteScalarAsync(cancellationToken) is long existingId
            ? existingId
            : throw new InvalidOperationException(
                "The outbound intent was neither stored nor found for its correlation, so the durable "
                + "Outbox could not be reconciled.");
    }

    private static void RequireSender(string sender, string parameterName)
    {
        if (sender is not (OutboxSenders.Ai or OutboxSenders.Agent or OutboxSenders.System))
        {
            throw new ArgumentException(
                $"The sender must be one of {OutboxSenders.Ai}, {OutboxSenders.Agent} or {OutboxSenders.System}.",
                parameterName);
        }
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value must not be empty.", parameterName);
        }
    }
}
