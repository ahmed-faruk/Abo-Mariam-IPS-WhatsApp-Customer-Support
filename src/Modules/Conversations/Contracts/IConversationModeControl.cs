namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The explicit mode-control seam of docs/TECHNICAL.md section 16. Releasing a conversation back to
/// the assistant, or closing it, is never inferred from a customer message: only this explicit
/// request changes the mode out of Human.
/// </summary>
public interface IConversationModeControl
{
    /// <summary>Releases a conversation that a human agent held back to automatic AI mode.</summary>
    Task<ConversationModeChangeOutcome> ReleaseToAiAsync(
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Closes a conversation. A closed conversation stays historical and is never reopened.</summary>
    Task<ConversationModeChangeOutcome> CloseAsync(
        long conversationId,
        CancellationToken cancellationToken = default);
}
