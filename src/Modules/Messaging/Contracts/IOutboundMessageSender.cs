namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// Sends one durable Outbox message. The Meta transport implements this later; the Messaging
/// worker never knows which provider is used.
/// </summary>
public interface IOutboundMessageSender
{
    /// <summary>
    /// Sends the message once. Returning a failed result or throwing both count as a failed
    /// attempt, and the worker records it through the Outbox store.
    /// </summary>
    Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default);
}
