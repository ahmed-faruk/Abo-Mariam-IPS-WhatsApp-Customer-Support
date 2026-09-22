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
/// <param name="ApplicationMetadata">
/// Bounded opaque metadata the sending module wants stored next to the immutable body, or null when it
/// wants none. Messaging persists it unchanged and never parses it: the meaning of the payload belongs
/// to the module that wrote it, so no Conversations concept leaks into the transport module.
/// </param>
public sealed record OutboundMessageRequest(
    long ConversationId,
    string CustomerExternalId,
    string CorrelationId,
    string Sender,
    string Body,
    string? ApplicationMetadata = null)
{
    /// <summary>
    /// The largest stored application metadata the queue accepts. It is deliberately tiny: the metadata
    /// is correlation state for a crash-safe retry, not a payload carrier, so it may hold identifiers and
    /// reference bookkeeping and nothing else. The stored column is bounded by the same value.
    /// </summary>
    public const int MaxApplicationMetadataLength = 2000;
}
