namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// Short-lived UX context (shortlist, filters, references). Never the authority for price or stock.
/// </summary>
public sealed class ConversationState
{
    public long ConversationId { get; set; }

    public string StateJson { get; set; } = "{}";

    public DateTime ExpiresAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Conversation? Conversation { get; set; }
}
