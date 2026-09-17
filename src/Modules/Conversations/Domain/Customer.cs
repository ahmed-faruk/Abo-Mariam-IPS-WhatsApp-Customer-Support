namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>A WhatsApp customer identified by their external number.</summary>
public sealed class Customer
{
    public long Id { get; set; }

    public string WhatsappNumber { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }
}
