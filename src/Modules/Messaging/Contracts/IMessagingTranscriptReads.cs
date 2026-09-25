namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The read-only Admin Lite transcript source of Issue #14. Inbound rows are not linked to a
/// conversation, so they are read by customer; outbound rows are read by conversation. This is only
/// correct for the reset-controlled single-conversation demo state (see src/Host.Web/Admin/README.md).
/// </summary>
public interface IMessagingTranscriptReads
{
    /// <summary>At most 500 inbound messages of a customer, by provider timestamp, then row id.</summary>
    Task<IReadOnlyList<InboundTranscriptEntry>> ListInboundByCustomerAsync(
        string customerExternalId,
        CancellationToken cancellationToken = default);

    /// <summary>At most 500 outbound messages of a conversation, by creation time, then row id.</summary>
    Task<IReadOnlyList<OutboundTranscriptEntry>> ListOutboundByConversationAsync(
        long conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>One inbound message of a transcript.</summary>
public sealed record InboundTranscriptEntry(
    long InboxMessageId,
    string ProviderMessageId,
    string MessageType,
    string? Body,
    DateTime ProviderTimestamp);

/// <summary>One outbound message of a transcript.</summary>
public sealed record OutboundTranscriptEntry(
    long OutboxMessageId,
    string Sender,
    string Body,
    string DeliveryStatus,
    DateTime CreatedAt);
