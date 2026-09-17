namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>Durable outbound acceptance: intent is stored before the transport can send it.</summary>
public interface IOutboundMessageQueue
{
    /// <summary>Stores the outbound intent and returns its durable Outbox message id.</summary>
    Task<long> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default);
}
