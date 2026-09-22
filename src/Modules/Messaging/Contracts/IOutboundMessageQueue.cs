namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>Durable outbound acceptance: intent is stored before the transport can send it.</summary>
public interface IOutboundMessageQueue
{
    /// <summary>
    /// Returns the durable reply already stored for one correlation, or null when no reply of that
    /// correlation is durable yet. A caller that is replaying a turn reads this before it produces new
    /// text, so an accepted reply is reconciled from what it really stored instead of being rendered
    /// again from facts that may have changed in the meantime. The acceptance names the conversation the
    /// row really belongs to, so a caller whose own conversation differs learns that it is looking at a
    /// reply of another conversation rather than at one of its own.
    /// </summary>
    Task<OutboundAcceptance?> FindByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the outbound intent and returns the acceptance that is durable afterwards. A correlation
    /// that already has a row reuses it, so the stored body and metadata are the original ones and the
    /// returned <see cref="OutboundAcceptance.IsExisting"/> is true. A reused row also keeps the
    /// conversation that accepted it, whatever conversation the request named.
    /// </summary>
    Task<OutboundAcceptance> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default);
}
