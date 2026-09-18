namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// Sends one durable Outbox message. The Meta transport implements this later; the Messaging
/// worker never knows which provider is used.
/// </summary>
/// <remarks>
/// The delivery of an Outbox message is identified by <see cref="ClaimedOutboxMessage.DeliveryKey"/>,
/// which stays the same across every retry and every later reclaim of that message. Implementations
/// must treat that key as the idempotency key of the logical delivery: the provider accepts at most
/// one message per key, so a repeated send for a key that was already accepted reconciles to the
/// already accepted delivery and returns its provider message id instead of delivering again. This
/// is what makes a send whose acknowledgement was lost safe to retry, so a provider adapter must not
/// report a successful send for a new delivery under a key that was already used.
/// </remarks>
public interface IOutboundMessageSender
{
    /// <summary>
    /// Delivers the message for the given delivery key. Returning a failed result or throwing both
    /// count as a failed attempt, and the worker records it through the Outbox store.
    /// </summary>
    Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default);
}
