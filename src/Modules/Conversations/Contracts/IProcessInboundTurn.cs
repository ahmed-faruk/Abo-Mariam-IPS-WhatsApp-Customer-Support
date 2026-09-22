namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The Conversations entry point of docs/TECHNICAL.md section 16. The Messaging worker calls it once per
/// claimed Inbox message and never learns what the reaction was.
/// </summary>
public interface IProcessInboundTurn
{
    /// <summary>
    /// Records and orchestrates one accepted inbound turn. Throwing fails the attempt, so the durable
    /// Inbox retries the same turn; the turn's reply is enqueued at most once because the Outbox
    /// correlates it with the inbound provider message id.
    /// </summary>
    Task<ConversationTurnResult> ProcessAsync(
        InboundTurn turn,
        CancellationToken cancellationToken = default);
}
