namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>Outbox sender values allowed by the <c>ck_outbox_sender</c> constraint.</summary>
public static class OutboxSenders
{
    public const string Ai = "AI";

    public const string Agent = "Agent";

    public const string System = "System";
}
