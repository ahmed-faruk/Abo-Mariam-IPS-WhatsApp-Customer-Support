namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// Sends one durable Outbox message. The Meta transport implements this later; the Messaging
/// worker never knows which provider is used.
/// </summary>
/// <remarks>
/// The delivery of an Outbox message is identified by <see cref="ClaimedOutboxMessage.DeliveryKey"/>,
/// which stays the same across every retry and every later reclaim of that message. It is an
/// application-level logical id for logging and local correlation. A provider adapter must not imply
/// that the provider honours it unless that provider exposes a documented idempotency primitive.
/// </remarks>
public interface IOutboundMessageSender
{
    /// <summary>
    /// Delivers the message once. The worker records the returned outcome through the Outbox store;
    /// unexpected transport exceptions are treated as unknown provider outcomes.
    /// </summary>
    Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default);
}
