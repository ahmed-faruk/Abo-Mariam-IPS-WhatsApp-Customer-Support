namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>The read-only Admin Lite conversation list of Issue #14.</summary>
public interface IConversationAdminReads
{
    /// <summary>Returns at most 200 conversations, most recent activity first.</summary>
    Task<IReadOnlyList<ConversationListRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one conversation, or null when it does not exist.</summary>
    Task<ConversationListRow?> GetAsync(long conversationId, CancellationToken cancellationToken = default);
}

/// <summary>One conversation as the Admin Lite list shows it.</summary>
/// <param name="LastActivityAt">The latest of the start, the last inbound and the last outbound time.</param>
public sealed record ConversationListRow(
    long ConversationId,
    string CustomerExternalId,
    ConversationMode Mode,
    DateTime StartedAt,
    DateTime LastActivityAt);
