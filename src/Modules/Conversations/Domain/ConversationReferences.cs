namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>Which part of the stored UX context a follow-up reference points at.</summary>
public enum ConversationReferenceTarget
{
    /// <summary>The first position of the stored shortlist.</summary>
    First,

    /// <summary>The second position of the stored shortlist.</summary>
    Second,

    /// <summary>The product the conversation currently references.</summary>
    Current,
}

/// <summary>One allowlisted way a customer can name a reference, already normalized.</summary>
/// <param name="NormalizedText">The normalized text that has to match exactly.</param>
/// <param name="Target">The part of the stored context the text points at.</param>
public sealed record ConversationReferenceAlias(string NormalizedText, ConversationReferenceTarget Target);

/// <summary>
/// The stable reason codes a clarification carries when a reference could not be resolved. They are
/// application-owned and bounded, so they can be logged and routed but never quote customer text.
/// </summary>
public static class ConversationReferenceReasons
{
    /// <summary>The text is not an allowlisted reference at all.</summary>
    public const string UnresolvedReference = "ReferenceNotResolved";

    /// <summary>The reference is a position, but no shortlist is stored any more.</summary>
    public const string ShortlistEmpty = "ShortlistEmpty";

    /// <summary>The reference asks for a position the stored shortlist does not have.</summary>
    public const string ShortlistPositionMissing = "ShortlistPositionMissing";

    /// <summary>The reference is "this one", but the conversation has no current reference.</summary>
    public const string CurrentReferenceMissing = "CurrentReferenceMissing";
}

/// <summary>
/// What a follow-up reference resolved to. A resolved reference carries catalogue identifiers only;
/// the current price, quantity and specifications are always read again from Catalog.
/// </summary>
public sealed record ConversationReferenceResolution
{
    private ConversationReferenceResolution()
    {
    }

    /// <summary>The resolved catalogue model id, or null when nothing was resolved.</summary>
    public long? ModelId { get; private init; }

    /// <summary>The resolved catalogue variant id, or null when nothing was resolved.</summary>
    public long? VariantId { get; private init; }

    /// <summary>The allowlisted target the reference named, when it named one.</summary>
    public ConversationReferenceTarget? Target { get; private init; }

    /// <summary>Why nothing was resolved, or null when the reference resolved.</summary>
    public string? ReasonCode { get; private init; }

    /// <summary>True when the reference resolved to a catalogue identifier pair.</summary>
    public bool IsResolved => ModelId is not null && VariantId is not null;

    /// <summary>A resolved reference.</summary>
    public static ConversationReferenceResolution Resolved(
        ConversationReferenceTarget target,
        long modelId,
        long variantId) =>
        new()
        {
            Target = target,
            ModelId = modelId,
            VariantId = variantId,
        };

    /// <summary>A reference that needs a clarification.</summary>
    public static ConversationReferenceResolution Unresolved(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return new ConversationReferenceResolution { ReasonCode = reasonCode };
    }
}

/// <summary>
/// Deterministic follow-up reference resolution over the explicit allowlist of the approved Issue #11
/// decision. Only allowlisted phrases resolve: there is no fuzzy matching, no similarity scoring and
/// no silent fallback, because guessing which product a customer meant is exactly the mistake this
/// rule exists to prevent.
/// </summary>
public static class ConversationReferences
{
    /// <summary>
    /// Every accepted reference phrase. The English forms and the Arabic demo forms of
    /// docs/PLAN.md UC-09 are listed explicitly; an unknown phrase stays unknown.
    /// </summary>
    public static IReadOnlyList<ConversationReferenceAlias> Aliases { get; } =
    [
        .. Assign(ConversationReferenceTarget.First, "first", "the first", "1st"),
        .. Assign(ConversationReferenceTarget.First, "الأولى", "الاولى", "الأول", "الاول"),
        .. Assign(ConversationReferenceTarget.Second, "second", "the second", "2nd"),
        .. Assign(ConversationReferenceTarget.Second, "التانية", "الثانية", "التاني", "الثاني"),
        .. Assign(ConversationReferenceTarget.Current, "current", "this", "this one", "the one"),
        .. Assign(ConversationReferenceTarget.Current, "دي", "ده", "الديل اللي قولتلي عليها"),
    ];

    /// <summary>
    /// Resolves one reference against the stored context. An unknown phrase, an empty shortlist, a
    /// position that is not stored and a missing current reference each produce a clarification with
    /// its own reason code.
    /// </summary>
    public static ConversationReferenceResolution Resolve(string? reference, ConversationStateDocument state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var normalized = Normalize(reference);
        var alias = normalized is null
            ? null
            : Aliases.FirstOrDefault(candidate => string.Equals(candidate.NormalizedText, normalized, StringComparison.Ordinal));

        if (alias is null)
        {
            return ConversationReferenceResolution.Unresolved(ConversationReferenceReasons.UnresolvedReference);
        }

        return alias.Target switch
        {
            ConversationReferenceTarget.First => Position(state, 0),
            ConversationReferenceTarget.Second => Position(state, 1),
            _ => Current(state),
        };
    }

    /// <summary>
    /// The deterministic comparison form of a reference: outer whitespace removed, internal whitespace
    /// runs folded to one space, and lowercased. It is a comparison rule only, so it cannot widen the
    /// allowlist.
    /// </summary>
    public static string? Normalize(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var parts = reference.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static ConversationReferenceResolution Position(ConversationStateDocument state, int index)
    {
        if (state.Shortlist.Count == 0)
        {
            return ConversationReferenceResolution.Unresolved(ConversationReferenceReasons.ShortlistEmpty);
        }

        if (state.Shortlist.Count <= index)
        {
            return ConversationReferenceResolution.Unresolved(ConversationReferenceReasons.ShortlistPositionMissing);
        }

        var entry = state.Shortlist[index];

        return ConversationReferenceResolution.Resolved(
            index == 0 ? ConversationReferenceTarget.First : ConversationReferenceTarget.Second,
            entry.ModelId,
            entry.VariantId);
    }

    /// <summary>
    /// "This one" resolves the product the conversation currently references. It never falls back to
    /// the first shortlist position, because that would answer about a product the customer did not mean.
    /// </summary>
    private static ConversationReferenceResolution Current(ConversationStateDocument state) =>
        state.LastModelId is { } modelId && state.LastVariantId is { } variantId
            ? ConversationReferenceResolution.Resolved(ConversationReferenceTarget.Current, modelId, variantId)
            : ConversationReferenceResolution.Unresolved(ConversationReferenceReasons.CurrentReferenceMissing);

    private static IEnumerable<ConversationReferenceAlias> Assign(
        ConversationReferenceTarget target,
        params string[] texts)
    {
        foreach (var text in texts)
        {
            yield return new ConversationReferenceAlias(
                Normalize(text) ?? throw new ArgumentException($"'{text}' normalizes to nothing.", nameof(texts)),
                target);
        }
    }
}
