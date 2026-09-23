namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// One inbound provider message as received by the transport, before it is durable.
/// The module owns the envelope hash and the queue partition key; callers only supply facts.
/// </summary>
/// <param name="RawBody">
/// The exact UTF-8 payload text used as the envelope hash input. The database stores it as JSONB, so
/// JSON formatting may be normalized there; callers must still pass the original request text here.
/// </param>
/// <param name="ProviderMessageId">The provider message id. Unique per inbound message.</param>
/// <param name="CustomerExternalId">The provider identifier of the sender.</param>
/// <param name="MessageType">The provider message type, for example <c>text</c>.</param>
/// <param name="ProviderTimestamp">The provider timestamp. Interpreted as UTC when no kind is set.</param>
/// <param name="Body">The message text when the provider supplies one.</param>
/// <param name="ConversationId">The already-known conversation, when the caller has resolved one.</param>
public sealed record InboundMessageEnvelope(
    string RawBody,
    string ProviderMessageId,
    string CustomerExternalId,
    string MessageType,
    DateTime ProviderTimestamp,
    string? Body = null,
    long? ConversationId = null);
