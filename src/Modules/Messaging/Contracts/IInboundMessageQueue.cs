namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// Durable inbound acceptance. Transport code calls this and only then answers the provider,
/// so the message survives a crash or a restart before any processing happens.
/// </summary>
public interface IInboundMessageQueue
{
    /// <summary>
    /// Stores the envelope and the Inbox message in one transaction. A repeated delivery of the
    /// same envelope body or the same provider message id is a safe no-op.
    /// </summary>
    Task<InboundEnqueueResult> EnqueueAsync(
        InboundMessageEnvelope envelope,
        CancellationToken cancellationToken = default);
}
