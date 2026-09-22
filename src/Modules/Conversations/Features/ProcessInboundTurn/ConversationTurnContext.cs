namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The customer and conversation one inbound turn is recorded on, plus the lifecycle values the
/// orchestration reads and refreshes. It is a working object for one turn and never leaves the module.
/// </summary>
internal sealed class ConversationTurnContext
{
    internal required long CustomerId { get; init; }

    internal required long ConversationId { get; init; }

    /// <summary>The stored mode value, one of <c>AI</c>, <c>Human</c> or <c>Closed</c>.</summary>
    internal required string Mode { get; set; }

    /// <summary>The service window after the accepted inbound was recorded.</summary>
    internal DateTime? WindowExpiresAt { get; set; }
}
