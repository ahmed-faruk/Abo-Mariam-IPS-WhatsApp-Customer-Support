namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// Processes one durable Inbox message. Conversation orchestration implements this later; the
/// Messaging worker never knows what the reaction is, it only guarantees the delivery.
/// </summary>
public interface IInboundMessageProcessor
{
    /// <summary>
    /// Handles one claimed message. Throwing marks the attempt as failed and schedules the
    /// documented retry.
    /// </summary>
    Task ProcessAsync(ClaimedInboxMessage message, CancellationToken cancellationToken = default);
}
