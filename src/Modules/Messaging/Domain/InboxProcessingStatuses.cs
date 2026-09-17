namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>Inbox statuses allowed by the <c>ck_inbox_status</c> constraint.</summary>
public static class InboxProcessingStatuses
{
    public const string Pending = "Pending";

    public const string Claimed = "Claimed";

    public const string Processed = "Processed";

    public const string Failed = "Failed";

    public const string DeadLettered = "DeadLettered";
}
