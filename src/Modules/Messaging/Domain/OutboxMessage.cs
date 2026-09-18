namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>
/// A durable outbound intent. Content is immutable after creation; only delivery bookkeeping changes.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    public long ConversationId { get; set; }

    public string CustomerExternalId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string Sender { get; set; } = OutboxSenders.Ai;

    public string Body { get; set; } = string.Empty;

    public byte[] BodyHash { get; set; } = [];

    public string? ProviderMessageId { get; set; }

    public string DeliveryStatus { get; set; } = OutboxDeliveryStatuses.Pending;

    public string PartitionKey { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTime RunAfter { get; set; }

    public DateTime? ClaimedAt { get; set; }

    /// <summary>The owner of the current claim. Only that owner may record an outcome.</summary>
    public Guid? ClaimToken { get; set; }

    /// <summary>When the current claim stops being owned by <see cref="ClaimToken"/>.</summary>
    public DateTime? ClaimExpiresAt { get; set; }

    public DateTime? SentAt { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
}
