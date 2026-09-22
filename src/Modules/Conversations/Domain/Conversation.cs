namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// A conversation between the assistant and one customer. At most one non-closed conversation
/// exists per customer.
/// </summary>
public sealed class Conversation
{
    public long Id { get; set; }

    public long CustomerId { get; set; }

    public string Mode { get; set; } = ConversationModes.Ai;

    /// <summary>
    /// How many explicit mode decisions this conversation has had. Every operator takeover, release and
    /// close, and every automatic handoff that really moved the mode, adds exactly one. It exists so a
    /// handoff that was made durable can tell "no one has decided since" from "an operator has decided
    /// since", which no timestamp can say: the lifecycle of an ordinary inbound turn moves those too.
    /// </summary>
    public long ModeRevision { get; set; }

    public DateTime? WindowExpiresAt { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? LastInboundAt { get; set; }

    public DateTime? LastOutboundAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Customer? Customer { get; set; }

    public ConversationState? State { get; set; }
}
