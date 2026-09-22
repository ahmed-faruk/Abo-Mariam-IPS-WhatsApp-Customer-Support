namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The outcome of one orchestrated inbound turn. It carries identifiers and a bounded outcome only, so
/// the Messaging worker can record the attempt without learning any business rule.
/// </summary>
/// <param name="ConversationId">The conversation the turn was recorded on.</param>
/// <param name="Mode">The conversation mode after the turn.</param>
/// <param name="Outcome">What the turn achieved.</param>
/// <param name="OutboxMessageId">
/// The durable Outbox message id when <see cref="ConversationTurnOutcome.ResponseEnqueued"/>; null otherwise.
/// The id is the existing row's id when an earlier attempt already enqueued this turn's reply.
/// </param>
public sealed record ConversationTurnResult(
    long ConversationId,
    ConversationMode Mode,
    ConversationTurnOutcome Outcome,
    long? OutboxMessageId = null);
