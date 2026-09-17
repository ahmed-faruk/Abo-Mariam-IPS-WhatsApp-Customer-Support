namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>An Outbox message that this worker now owns exclusively.</summary>
/// <param name="Id">The Outbox message id, also the deterministic send order.</param>
/// <param name="ConversationId">The conversation the reply belongs to.</param>
/// <param name="CustomerExternalId">The provider identifier of the recipient.</param>
/// <param name="CorrelationId">Correlates the reply with the inbound turn that produced it.</param>
/// <param name="Sender">One of <c>AI</c>, <c>Agent</c> or <c>System</c>.</param>
/// <param name="Body">The stored reply text.</param>
/// <param name="ProviderMessageId">Set only when the message was already sent once.</param>
/// <param name="Attempts">Attempts including this claim.</param>
/// <param name="MaxAttempts">The stored attempt limit for this message.</param>
public sealed record ClaimedOutboxMessage(
    long Id,
    long ConversationId,
    string CustomerExternalId,
    string CorrelationId,
    string Sender,
    string Body,
    string? ProviderMessageId,
    int Attempts,
    int MaxAttempts);
