namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// One durable Outbox row as the queue really stored it, returned whether this call inserted the row or
/// found an earlier immutable one for the same correlation. The stored body and the stored application
/// metadata are the authoritative pair: a replayed turn reconciles against them instead of against the
/// values it tried to write, because an already accepted reply is never rewritten.
/// </summary>
/// <param name="OutboxMessageId">The durable identity of the stored reply.</param>
/// <param name="ConversationId">
/// The conversation that accepted the stored reply, exactly as the row holds it. It is the stored value
/// rather than the caller's, because a correlation that already has a row keeps the conversation that
/// created it: a caller whose own conversation differs is looking at somebody else's durable reply.
/// </param>
/// <param name="Body">The immutable reply text of that row.</param>
/// <param name="ApplicationMetadata">The immutable opaque metadata of that row, or null when it has none.</param>
/// <param name="IsExisting">
/// True when the correlation already had a durable row, so this call replayed an accepted reply instead
/// of storing a new one.
/// </param>
public sealed record OutboundAcceptance(
    long OutboxMessageId,
    long ConversationId,
    string Body,
    string? ApplicationMetadata,
    bool IsExisting);
