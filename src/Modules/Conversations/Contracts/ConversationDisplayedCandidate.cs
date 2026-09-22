namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// One product a reply really displayed, as the ordered pair of catalogue identifiers the customer saw
/// it at. A list of these is what a conversation may reference afterwards, so it carries identifiers and
/// position only: price, quantity, grade, warranty and specifications stay in Catalog.
/// </summary>
/// <param name="ModelId">The catalogue model id of the displayed product.</param>
/// <param name="VariantId">The catalogue variant id the displayed product resolved to.</param>
public sealed record ConversationDisplayedCandidate(long ModelId, long VariantId);
