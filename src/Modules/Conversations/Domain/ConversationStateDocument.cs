using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// The short-lived UX state of one conversation, as documented in docs/TECHNICAL.md section 13. It
/// holds identifiers, ordering, the customer's stated filters and the current reference, and nothing
/// else: price, quantity, availability, warranty, exact specifications and Storefront answers are
/// always reloaded from their owning module and can never be stored here.
/// </summary>
public sealed record ConversationStateDocument
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The state of a conversation that has no usable context: first turn, expired or malformed.</summary>
    public static ConversationStateDocument Empty { get; } = new();

    /// <summary>The shortlist the previous turn showed, ordered by display position.</summary>
    [JsonPropertyName("shortlist")]
    public IReadOnlyList<ConversationShortlistEntry> Shortlist { get; init; } = [];

    /// <summary>The current reference: the model id of the product the conversation last resolved.</summary>
    [JsonPropertyName("lastModelId")]
    public long? LastModelId { get; init; }

    /// <summary>The variant of <see cref="LastModelId"/> the conversation last resolved.</summary>
    [JsonPropertyName("lastVariantId")]
    public long? LastVariantId { get; init; }

    /// <summary>The intent of the previous turn, for diagnostics only.</summary>
    [JsonPropertyName("lastIntent")]
    public string? LastIntent { get; init; }

    /// <summary>The filters the customer stated last, so a follow-up can be read as a refinement.</summary>
    [JsonPropertyName("lastFilters")]
    public ConversationStateFilters? LastFilters { get; init; }

    /// <summary>True when the document carries no usable context at all.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        Shortlist.Count == 0
        && LastModelId is null
        && LastVariantId is null
        && LastIntent is null
        && LastFilters is null;

    /// <summary>
    /// Builds the ordered shortlist of a search result. Positions are contiguous one-based display
    /// positions and a model appears at most once, so the numbering a customer heard cannot shift
    /// between turns.
    /// </summary>
    public static IReadOnlyList<ConversationShortlistEntry> BuildShortlist(
        IEnumerable<(long ModelId, long VariantId)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var entries = new List<ConversationShortlistEntry>();
        var seen = new HashSet<long>();

        foreach (var (modelId, variantId) in candidates)
        {
            if (modelId <= 0 || variantId <= 0 || !seen.Add(modelId))
            {
                continue;
            }

            entries.Add(new ConversationShortlistEntry
            {
                Position = entries.Count + 1,
                ModelId = modelId,
                VariantId = variantId,
            });
        }

        return entries;
    }

    /// <summary>The stored JSON of this document, written the same way every time.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reads a stored document. Malformed, empty, wrongly shaped or structurally unsound JSON is treated
    /// as empty state instead of throwing, because an unreadable UX context must never break an inbound
    /// turn. Unknown properties are dropped, so a hand-edited or legacy document cannot smuggle a
    /// commercial fact into the orchestration.
    /// </summary>
    public static ConversationStateDocument Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ConversationStateDocument>(json, SerializerOptions);

            if (document is null || !document.TryNormalize(out var normalized))
            {
                return Empty;
            }

            return normalized;
        }
        catch (JsonException)
        {
            return Empty;
        }
        catch (NotSupportedException)
        {
            return Empty;
        }
    }

    /// <summary>Value equality, because the stored collections are compared by content rather than by instance.</summary>
    public bool Equals(ConversationStateDocument? other) =>
        other is not null
        && LastModelId == other.LastModelId
        && LastVariantId == other.LastVariantId
        && string.Equals(LastIntent, other.LastIntent, StringComparison.Ordinal)
        && Equals(LastFilters, other.LastFilters)
        && Shortlist.Count == other.Shortlist.Count
        && Shortlist.SequenceEqual(other.Shortlist);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(LastModelId);
        hash.Add(LastVariantId);
        hash.Add(LastIntent, StringComparer.Ordinal);
        hash.Add(LastFilters);

        foreach (var entry in Shortlist)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Validates the stored shape and returns the document a turn may use. A document that is not
    /// structurally sound is never partially trusted: a null collection, a shortlist whose numbering or
    /// identifiers cannot be trusted, or a budget that contradicts its own type makes the whole document
    /// empty. That matters because a positional reference is resolved by the number the customer already
    /// heard, and because a stored budget is reused by the next refinement of a search.
    /// </summary>
    private bool TryNormalize(out ConversationStateDocument normalized)
    {
        normalized = Empty;

        if (HasNullEntry(Shortlist) || (LastFilters is { } filters && !IsStructurallyValid(filters)))
        {
            return false;
        }

        var ordered = Shortlist.OrderBy(entry => entry.Position).ToList();
        var seenModels = new HashSet<long>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];

            // The shortlist is always written as a contiguous one-based display order with one entry per
            // model, so anything else was not written by this application and cannot be resolved against.
            if (entry.Position != index + 1
                || entry.ModelId <= 0
                || entry.VariantId <= 0
                || !seenModels.Add(entry.ModelId))
            {
                return false;
            }
        }

        normalized = this with { Shortlist = ordered };

        return true;
    }

    /// <summary>The stored filters, which a later turn reuses, so they have to be structurally sound.</summary>
    private static bool IsStructurallyValid(ConversationStateFilters filters)
    {
        if (HasBlankEntry(filters.RequiredPorts) || HasBlankEntry(filters.Grades))
        {
            return false;
        }

        return filters.BudgetType switch
        {
            null or BudgetType.None => filters.BudgetTarget is null
                && filters.BudgetMin is null
                && filters.BudgetMax is null,
            BudgetType.Soft or BudgetType.Hard => filters.BudgetTarget is > 0m
                && filters.BudgetMin is null
                && filters.BudgetMax is null,
            BudgetType.Range => filters.BudgetTarget is null
                && filters.BudgetMin is > 0m
                && filters.BudgetMax is > 0m
                && filters.BudgetMin <= filters.BudgetMax,
            _ => false,
        };
    }

    private static bool HasNullEntry<T>(IReadOnlyList<T>? values)
        where T : class =>
        values is null || values.Any(value => value is null);

    private static bool HasBlankEntry(IReadOnlyList<string>? values) =>
        values is null || values.Any(string.IsNullOrWhiteSpace);
}

/// <summary>One position of the shortlist a previous turn showed.</summary>
public sealed record ConversationShortlistEntry
{
    /// <summary>The one-based position the customer saw.</summary>
    [JsonPropertyName("position")]
    public int Position { get; init; }

    /// <summary>The catalogue model id at that position.</summary>
    [JsonPropertyName("modelId")]
    public long ModelId { get; init; }

    /// <summary>The catalogue variant id the position resolved to.</summary>
    [JsonPropertyName("variantId")]
    public long VariantId { get; init; }
}

/// <summary>
/// The filters the customer stated on the previous turn. They are the customer's request, not a
/// commercial fact: a later turn may reuse them as context, and Catalog still normalizes and resolves
/// every one of them against current data.
/// </summary>
public sealed record ConversationStateFilters
{
    [JsonPropertyName("brand")]
    public string? Brand { get; init; }

    [JsonPropertyName("modelCode")]
    public string? ModelCode { get; init; }

    [JsonPropertyName("sizeInches")]
    public decimal? SizeInches { get; init; }

    [JsonPropertyName("panel")]
    public string? Panel { get; init; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("minRefreshRate")]
    public int? MinRefreshRate { get; init; }

    [JsonPropertyName("requiredPorts")]
    public IReadOnlyList<string> RequiredPorts { get; init; } = [];

    [JsonPropertyName("grades")]
    public IReadOnlyList<string> Grades { get; init; } = [];

    [JsonPropertyName("budgetType")]
    public BudgetType? BudgetType { get; init; }

    [JsonPropertyName("budgetTarget")]
    public decimal? BudgetTarget { get; init; }

    [JsonPropertyName("budgetMin")]
    public decimal? BudgetMin { get; init; }

    [JsonPropertyName("budgetMax")]
    public decimal? BudgetMax { get; init; }

    [JsonPropertyName("useCase")]
    public string? UseCase { get; init; }

    /// <summary>Value equality, because the stored collections are compared by content.</summary>
    public bool Equals(ConversationStateFilters? other) =>
        other is not null
        && string.Equals(Brand, other.Brand, StringComparison.Ordinal)
        && string.Equals(ModelCode, other.ModelCode, StringComparison.Ordinal)
        && SizeInches == other.SizeInches
        && string.Equals(Panel, other.Panel, StringComparison.Ordinal)
        && string.Equals(Resolution, other.Resolution, StringComparison.Ordinal)
        && MinRefreshRate == other.MinRefreshRate
        && BudgetType == other.BudgetType
        && BudgetTarget == other.BudgetTarget
        && BudgetMin == other.BudgetMin
        && BudgetMax == other.BudgetMax
        && string.Equals(UseCase, other.UseCase, StringComparison.Ordinal)
        && RequiredPorts.SequenceEqual(other.RequiredPorts, StringComparer.Ordinal)
        && Grades.SequenceEqual(other.Grades, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Brand, StringComparer.Ordinal);
        hash.Add(ModelCode, StringComparer.Ordinal);
        hash.Add(SizeInches);
        hash.Add(Panel, StringComparer.Ordinal);
        hash.Add(Resolution, StringComparer.Ordinal);
        hash.Add(MinRefreshRate);
        hash.Add(BudgetType);
        hash.Add(BudgetTarget);
        hash.Add(BudgetMin);
        hash.Add(BudgetMax);
        hash.Add(UseCase, StringComparer.Ordinal);

        foreach (var port in RequiredPorts)
        {
            hash.Add(port, StringComparer.Ordinal);
        }

        foreach (var grade in Grades)
        {
            hash.Add(grade, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
