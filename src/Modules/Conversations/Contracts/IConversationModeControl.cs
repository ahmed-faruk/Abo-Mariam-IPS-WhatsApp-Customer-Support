namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The explicit mode-control seam of docs/TECHNICAL.md section 16 and section 18: the operator actions
/// of the Admin conversations screen. Taking a conversation over, releasing it back to the assistant and
/// closing it are never inferred from a customer message, and every one of them is coordinated with the
/// automatic replies of the same conversation, so a mode change can never be overtaken by a turn that
/// had already read the old mode.
/// </summary>
public interface IConversationModeControl
{
    /// <summary>
    /// Takes over an automatic conversation, so a human agent owns it from now on and no automatic
    /// reply is produced. Taking over a conversation a human already owns changes nothing, and a
    /// closed conversation stays historical.
    /// </summary>
    Task<ConversationModeChangeOutcome> TakeOverAsync(
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a conversation that a human agent held back to automatic AI mode.</summary>
    Task<ConversationModeChangeOutcome> ReleaseToAiAsync(
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Closes a conversation. A closed conversation stays historical and is never reopened.</summary>
    Task<ConversationModeChangeOutcome> CloseAsync(
        long conversationId,
        CancellationToken cancellationToken = default);
}
