namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// What one orchestrated turn decided the customer should be told, expressed as routing data instead
/// of prose. It deliberately carries identifiers, canonical keys and bounded reason codes only: price,
/// quantity, availability, warranty, exact specifications and business answers stay owned by
/// PostgreSQL and are reloaded by the renderer immediately before the reply is built.
/// </summary>
public sealed record ConversationResponseIntent
{
    /// <summary>Which deterministic reply the renderer must build.</summary>
    public required ConversationResponseKind Kind { get; init; }

    /// <summary>The conversation the reply belongs to.</summary>
    public required long ConversationId { get; init; }

    /// <summary>The recipient's provider identifier.</summary>
    public required string CustomerExternalId { get; init; }

    /// <summary>The one identified product, when the reply is about a single product.</summary>
    public long? ModelId { get; init; }

    /// <summary>The chosen variant of <see cref="ModelId"/>, when one was resolved.</summary>
    public long? VariantId { get; init; }

    /// <summary>
    /// The ordered shortlist of model ids a search produced, or the candidates a comparison covers.
    /// The position of an id is its one-based position in the customer-facing list.
    /// </summary>
    public IReadOnlyList<long> ModelIds { get; init; } = [];

    /// <summary>The ordered shortlist variants matching <see cref="ModelIds"/> one to one.</summary>
    public IReadOnlyList<long> VariantIds { get; init; } = [];

    /// <summary>The approved Storefront key a business-info reply must be read from.</summary>
    public string? StorefrontKey { get; init; }

    /// <summary>
    /// A short bounded application reason code such as <c>ReferenceNotResolved</c>. It explains why a
    /// clarification or an unavailable reply was chosen; it never contains customer or model text.
    /// </summary>
    public string? ReasonCode { get; init; }
}
