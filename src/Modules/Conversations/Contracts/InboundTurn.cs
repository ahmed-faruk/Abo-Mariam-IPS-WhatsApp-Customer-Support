namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// One accepted inbound customer turn that Conversations must orchestrate. It carries only the facts
/// the Messaging Inbox already stored, so no Messaging queue type crosses this boundary.
/// </summary>
/// <param name="ProviderMessageId">The provider message id; the correlation id of the turn's reply.</param>
/// <param name="CustomerExternalId">The provider identifier of the sender.</param>
/// <param name="MessageType">The provider message type, for example <c>text</c>.</param>
/// <param name="ProviderTimestamp">
/// The provider timestamp. It is interpreted as UTC when no kind is set, because the service window is
/// refreshed from it.
/// </param>
/// <param name="Body">The message text when the provider supplied one.</param>
/// <param name="ConversationId">The already-known conversation, when the inbound envelope carried one.</param>
public sealed record InboundTurn(
    string ProviderMessageId,
    string CustomerExternalId,
    string MessageType,
    DateTime ProviderTimestamp,
    string? Body = null,
    long? ConversationId = null);
