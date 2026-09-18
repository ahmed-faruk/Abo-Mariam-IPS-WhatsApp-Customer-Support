using System.Globalization;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>Independent expected values for the Messaging queue tests.</summary>
internal static class MessagingSamples
{
    public static readonly DateTime ProviderTimestamp =
        new(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

    public static InboundMessageEnvelope Inbound(
        string providerMessageId,
        string customerExternalId,
        string? body = "hello",
        long? conversationId = null,
        DateTime? providerTimestamp = null) =>
        new(
            RawBody: string.Create(
                CultureInfo.InvariantCulture,
                $"{{ \"id\": \"{providerMessageId}\", \"from\": \"{customerExternalId}\", \"text\": \"{body}\" }}"),
            ProviderMessageId: providerMessageId,
            CustomerExternalId: customerExternalId,
            MessageType: "text",
            ProviderTimestamp: providerTimestamp ?? ProviderTimestamp,
            Body: body,
            ConversationId: conversationId);

    public static OutboundMessageRequest Outbound(
        long conversationId,
        string customerExternalId,
        string body = "your reply",
        string sender = OutboxSenders.Ai) =>
        new(
            ConversationId: conversationId,
            CustomerExternalId: customerExternalId,
            CorrelationId: string.Create(CultureInfo.InvariantCulture, $"corr-{conversationId}"),
            Sender: sender,
            Body: body);
}
