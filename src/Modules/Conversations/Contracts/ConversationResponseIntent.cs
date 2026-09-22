using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

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

    /// <summary>An intent with no candidates yet, which is every reply that is not a comparison.</summary>
    public ConversationResponseIntent()
    {
    }

    [SetsRequiredMembers]
    private ConversationResponseIntent(
        ConversationResponseIntent source,
        ImmutableArray<long> modelIds,
        ImmutableArray<long> variantIds,
        ProductSearchQuery? searchQuery)
    {
        Kind = source.Kind;
        ConversationId = source.ConversationId;
        CustomerExternalId = source.CustomerExternalId;
        ModelId = source.ModelId;
        VariantId = source.VariantId;
        SearchQuery = searchQuery ?? source.SearchQuery;
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
    /// The candidates a comparison covers, in the order the comparison names them. The position of an id
    /// is its one-based position in the comparison. The list is an immutable snapshot taken when the
    /// intent was built, so it can never change underneath a renderer.
    /// <para>
    /// A product search deliberately carries nothing here. What a search reply may display is decided by
    /// the final search the renderer runs immediately before the reply is stored, so a shortlist a turn
    /// merely intended to display can never become the list the customer is told they saw.
    /// </para>
    /// </summary>
    public IReadOnlyList<long> ModelIds => modelIds;

    /// <summary>
    /// The ordered comparison variants matching <see cref="ModelIds"/> one to one. It is snapshotted
    /// together with the model ids, so the two lists cannot drift out of step.
    /// </summary>
    public IReadOnlyList<long> VariantIds => variantIds;

    /// <summary>
    /// The effective search a product-search reply must be answered from: the customer's stated filters
    /// merged with the filters the conversation already held. The renderer runs exactly this search again
    /// immediately before the reply is stored, so Catalog owns every eligibility rule - active model and
    /// variant, stock, hard and soft budget, required ports, grades and ranking - from one authoritative
    /// query instead of a copy of those rules living here.
    /// </summary>
    public ProductSearchQuery? SearchQuery { get; init; }

    /// <summary>The approved Storefront key a business-info reply must be read from.</summary>
    public string? StorefrontKey { get; init; }

    /// <summary>
    /// A short bounded application reason code such as <c>ReferenceNotResolved</c>. It explains why a
    /// clarification or an unavailable reply was chosen; it never contains customer or model text.
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The same intent carrying one ordered list of candidates, which is how a comparison names the
    /// products it covers. Model ids and variant ids are positional pairs, so they are always taken
    /// together from one sequence and stored as immutable snapshots: a caller that keeps its own collection,
    /// or mutates it after this call, cannot change the reply, and a pair can never be half written.
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

        return new ConversationResponseIntent(this, [.. models], [.. variants], searchQuery: null);
    }

    /// <summary>
    /// The same intent carrying the effective search it must be answered from. The query is deep-copied,
    /// including its port and grade collections, so a caller that keeps its own query, or mutates the
    /// collections it passed in, cannot change the search the renderer runs.
    /// </summary>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public ConversationResponseIntent WithSearchQuery(ProductSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The collections are replaced with immutable copies, so the snapshot cannot be mutated through
        // the caller's own list, which is the only way the effective search could drift after routing.
        var snapshot = query with
        {
            RequiredPorts = ImmutableArray.CreateRange(query.RequiredPorts),
            Grades = ImmutableArray.CreateRange(query.Grades),
        };

        return new ConversationResponseIntent(this, modelIds, variantIds, snapshot);
    }
}
