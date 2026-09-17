namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>A raw inbound webhook body, deduplicated by its content hash.</summary>
public sealed class WebhookEnvelope
{
    public long Id { get; set; }

    public byte[] EnvelopeHash { get; set; } = [];

    public string RawBody { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
