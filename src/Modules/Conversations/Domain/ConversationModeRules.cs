namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// The AI/Human/Closed transitions of docs/TECHNICAL.md section 16. Only the assistant answers
/// automatically, only an explicit request leaves Human, and a closed conversation is never reopened.
/// </summary>
public static class ConversationModeRules
{
    /// <summary>True when the assistant may interpret the message and answer on its own.</summary>
    public static bool AnswersAutomatically(string mode) =>
        string.Equals(mode, ConversationModes.Ai, StringComparison.Ordinal);

    /// <summary>A human handoff always ends in Human mode, whether the turn was automatic or already held.</summary>
    public static string AfterHandoff(string currentMode) => ConversationModes.Human;

    /// <summary>
    /// The mode after an explicit release request, or null when the request cannot apply. A closed
    /// conversation stays closed: releasing it would reopen historical state.
    /// </summary>
    public static string? AfterExplicitRelease(string currentMode) =>
        string.Equals(currentMode, ConversationModes.Closed, StringComparison.Ordinal)
            ? null
            : ConversationModes.Ai;

    /// <summary>The mode after an explicit close request, or null when the conversation is already closed.</summary>
    public static string? AfterClose(string currentMode) =>
        string.Equals(currentMode, ConversationModes.Closed, StringComparison.Ordinal)
            ? null
            : ConversationModes.Closed;
}
