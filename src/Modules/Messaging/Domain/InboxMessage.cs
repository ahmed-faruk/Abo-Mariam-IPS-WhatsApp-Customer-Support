namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>An inbound message awaiting processing. The provider message id is unique.</summary>
public sealed class InboxMessage
{
    public long Id { get; set; }

    public long EnvelopeId { get; set; }

    public string ProviderMessageId { get; set; } = string.Empty;

    public string CustomerExternalId { get; set; } = string.Empty;

    public long? ConversationId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string? Body { get; set; }

    public DateTime ProviderTimestamp { get; set; }

    public string ProcessingStatus { get; set; } = InboxProcessingStatuses.Pending;

    public string PartitionKey { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime RunAfter { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? LastError { get; set; }

    public DateTime ReceivedAt { get; set; }

    public WebhookEnvelope? Envelope { get; set; }
}
