namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// An outbound intent that must be durable before any transport send is attempted.
/// The module owns the body hash and the queue partition key.
/// </summary>
/// <param name="ConversationId">The conversation the reply belongs to.</param>
/// <param name="CustomerExternalId">The provider identifier of the recipient.</param>
/// <param name="CorrelationId">Correlates the reply with the inbound turn that produced it.</param>
/// <param name="Sender">One of <c>AI</c>, <c>Agent</c> or <c>System</c>.</param>
/// <param name="Body">The exact reply text. Immutable once stored.</param>
public sealed record OutboundMessageRequest(
    long ConversationId,
    string CustomerExternalId,
    string CorrelationId,
    string Sender,
    string Body);
