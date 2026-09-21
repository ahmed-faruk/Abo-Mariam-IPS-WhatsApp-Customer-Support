namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>What an explicit mode-control request did.</summary>
public enum ConversationModeChangeOutcome
{
    /// <summary>The mode changed and was committed.</summary>
    Changed,

    /// <summary>The conversation already was in the requested mode, so nothing was written.</summary>
    Unchanged,

    /// <summary>No such conversation exists, so nothing was written.</summary>
    NotFound,
}
