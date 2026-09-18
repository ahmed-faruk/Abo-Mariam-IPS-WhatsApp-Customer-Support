namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The durable outcome of accepting an inbound message.
/// A duplicate delivery resolves to the Inbox message that is already stored.
/// </summary>
/// <param name="InboxMessageId">The durable Inbox message id, including for duplicates.</param>
/// <param name="IsDuplicate">True when the provider message id was already stored.</param>
public sealed record InboundEnqueueResult(long InboxMessageId, bool IsDuplicate);
