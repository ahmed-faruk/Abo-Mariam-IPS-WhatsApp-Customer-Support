namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>An Inbox message that this worker now owns exclusively.</summary>
/// <param name="Id">The Inbox message id, also the deterministic processing order.</param>
/// <param name="ProviderMessageId">The provider message id, already deduplicated.</param>
/// <param name="CustomerExternalId">The provider identifier of the sender.</param>
/// <param name="ConversationId">The conversation id when the envelope already carried one.</param>
/// <param name="MessageType">The provider message type.</param>
/// <param name="Body">The message text when the provider supplied one.</param>
/// <param name="ProviderTimestamp">The provider timestamp, in UTC.</param>
/// <param name="Attempts">Attempts including this claim.</param>
/// <param name="ClaimToken">
/// The lease owner of this claim. Completion and failure must present it, so an owner whose lease
/// expired can never record an outcome for the claim somebody else holds now.
/// </param>
public sealed record ClaimedInboxMessage(
    long Id,
    string ProviderMessageId,
    string CustomerExternalId,
    long? ConversationId,
    string MessageType,
    string? Body,
    DateTime ProviderTimestamp,
    int Attempts,
    Guid ClaimToken);
