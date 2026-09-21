using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// What one orchestrated turn decided the customer should be told, expressed as routing data instead
/// of prose. It deliberately carries identifiers, canonical keys and bounded reason codes only: price,
/// quantity, availability, warranty, exact specifications and business answers stay owned by
/// PostgreSQL and are reloaded by the renderer immediately before the reply is built.
/// </summary>
public sealed record ConversationResponseIntent
{
    private readonly ImmutableArray<long> modelIds = [];

    private readonly ImmutableArray<long> variantIds = [];

    /// <summary>An intent with no candidates yet, which is every reply that is not about a shortlist.</summary>
    public ConversationResponseIntent()
    {
    }

    [SetsRequiredMembers]
    private ConversationResponseIntent(
        ConversationResponseIntent source,
        ImmutableArray<long> modelIds,
        ImmutableArray<long> variantIds)
    {
        Kind = source.Kind;
        ConversationId = source.ConversationId;
        CustomerExternalId = source.CustomerExternalId;
        ModelId = source.ModelId;
        VariantId = source.VariantId;
        StorefrontKey = source.StorefrontKey;
        ReasonCode = source.ReasonCode;
        this.modelIds = modelIds;
        this.variantIds = variantIds;
    }

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
    /// The position of an id is its one-based position in the customer-facing list. The list is an
    /// immutable snapshot taken when the intent was built, so it can never change underneath a renderer.
    /// </summary>
    public IReadOnlyList<long> ModelIds => modelIds;

    /// <summary>
    /// The ordered shortlist variants matching <see cref="ModelIds"/> one to one. It is snapshotted
    /// together with the model ids, so the two lists cannot drift out of step.
    /// </summary>
    public IReadOnlyList<long> VariantIds => variantIds;

    /// <summary>The approved Storefront key a business-info reply must be read from.</summary>
    public string? StorefrontKey { get; init; }

    /// <summary>
    /// A short bounded application reason code such as <c>ReferenceNotResolved</c>. It explains why a
    /// clarification or an unavailable reply was chosen; it never contains customer or model text.
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The same intent carrying one ordered shortlist. Model ids and variant ids are positional pairs, so
    /// they are always taken together from one sequence and stored as immutable snapshots: a caller that
    /// keeps its own collection, or mutates it after this call, cannot change the reply, and a pair can
    /// never be half written.
    /// </summary>
    /// <exception cref="ArgumentNullException">The candidate sequence is null.</exception>
    public ConversationResponseIntent WithCandidates(IEnumerable<(long ModelId, long VariantId)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var pairs = candidates as IReadOnlyCollection<(long ModelId, long VariantId)> ?? [.. candidates];
        var models = new long[pairs.Count];
        var variants = new long[pairs.Count];
        var index = 0;

        foreach (var (modelId, variantId) in pairs)
        {
            models[index] = modelId;
            variants[index] = variantId;
            index++;
        }

        return new ConversationResponseIntent(this, [.. models], [.. variants]);
    }
}
