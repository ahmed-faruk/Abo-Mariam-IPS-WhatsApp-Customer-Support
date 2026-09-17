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
internal sealed class OutboundMessageQueue(MessagingDbContext dbContext) : IOutboundMessageQueue
{
    private const string InsertOutboxSql = """
        INSERT INTO messaging.outbox_message
            (conversation_id, customer_external_id, correlation_id, sender, body, body_hash, partition_key)
        VALUES
            (@conversation_id, @customer_external_id, @correlation_id, @sender, @body, @body_hash, @partition_key)
        RETURNING id;
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

        // A single INSERT is the whole transaction: once it returns, the reply survives a crash.
        await using var command = MessagingQueueCommands.Create(
            await MessagingQueueCommands.OpenAsync(dbContext, cancellationToken), null, InsertOutboxSql);
        MessagingQueueCommands.Add(command, "conversation_id", request.ConversationId);
        MessagingQueueCommands.Add(command, "customer_external_id", request.CustomerExternalId);
        MessagingQueueCommands.Add(command, "correlation_id", request.CorrelationId);
        MessagingQueueCommands.Add(command, "sender", request.Sender);
        MessagingQueueCommands.Add(command, "body", request.Body);
        MessagingQueueCommands.Add(command, "body_hash", SHA256.HashData(Encoding.UTF8.GetBytes(request.Body)));
        MessagingQueueCommands.Add(command, "partition_key", request.ConversationId.ToString(CultureInfo.InvariantCulture));

        return await command.ExecuteScalarAsync(cancellationToken) is long id
            ? id
            : throw new InvalidOperationException("The outbound intent was not stored.");
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
