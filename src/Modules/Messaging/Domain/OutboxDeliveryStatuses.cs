namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>Outbox statuses allowed by the <c>ck_outbox_status</c> constraint.</summary>
public static class OutboxDeliveryStatuses
{
    public const string Pending = "Pending";

    public const string Claimed = "Claimed";

    public const string Sent = "Sent";

    public const string Failed = "Failed";

    public const string DeadLettered = "DeadLettered";
}
