namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The sender values an outbound intent may carry. They are exposed here because a caller outside
/// Messaging has to name one when it stores an outbound intent; the stored values and the
/// <c>ck_outbox_sender</c> constraint are built from these same names, so they cannot drift.
/// </summary>
public static class OutboundSenderNames
{
    /// <summary>The assistant answered automatically.</summary>
    public const string Ai = "AI";

    /// <summary>A human agent answered.</summary>
    public const string Agent = "Agent";

    /// <summary>The system produced a non-conversational message.</summary>
    public const string System = "System";
}
