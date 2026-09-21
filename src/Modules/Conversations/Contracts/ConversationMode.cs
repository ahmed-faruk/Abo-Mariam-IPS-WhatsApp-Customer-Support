namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The customer-facing conversation modes of docs/TECHNICAL.md section 6.3. The stored rows keep the
/// documented text values; this enum is the model-neutral shape other modules may see.
/// </summary>
public enum ConversationMode
{
    /// <summary>The assistant answers automatically.</summary>
    Ai,

    /// <summary>A human agent owns the conversation, so no automatic reply is produced.</summary>
    Human,

    /// <summary>The conversation is historical and is never reopened.</summary>
    Closed,
}
