using System.Text;

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

        return alias is null
            ? ConversationReferenceResolution.Unresolved(ConversationReferenceReasons.UnresolvedReference)
            : ResolveTarget(alias.Target, state);
    }

    /// <summary>
    /// Resolves a model-produced reference with one bounded recovery step. When the reference is not an
    /// allowlisted alias at all, the original customer text is searched for <em>existing</em> aliases
    /// with whole-token and whole-phrase matching: exactly one distinct allowlisted target resolves
    /// against the stored state, zero or several targets keep the original resolution. Whenever normal
    /// resolution fails for any reason, the text may name the product instead — a model reference that
    /// is allowlisted but whose target the stored state cannot answer is exactly such a case. There is
    /// no fuzzy matching, no substring matching, no new alias, and the text is not stored.
    /// </summary>
    public static ConversationReferenceResolution ResolveWithCustomerText(
        string? reference,
        string? customerText,
        ConversationStateDocument state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var resolution = Resolve(reference, state);

        if (resolution.IsResolved || Normalize(reference) is null)
        {
            return resolution;
        }

        var tokens = Tokens(customerText);
        var targets = Aliases
            .Where(alias => ContainsAlias(tokens, alias))
            .Select(alias => alias.Target)
            .Distinct()
            .ToList();

        return targets.Count == 1 ? ResolveTarget(targets[0], state) : resolution;
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

    private static ConversationReferenceResolution ResolveTarget(
        ConversationReferenceTarget target,
        ConversationStateDocument state) =>
        target switch
        {
            ConversationReferenceTarget.First => Position(state, 0),
            ConversationReferenceTarget.Second => Position(state, 1),
            _ => Current(state),
        };

    /// <summary>
    /// The token form of a customer message for alias matching: split on whitespace and punctuation and
    /// lowercased, so <c>دي</c> can never match inside <c>ديل</c>. It is a matching rule only and cannot
    /// widen the allowlist.
    /// </summary>
    private static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(char.ToLowerInvariant(character));
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static bool ContainsAlias(IReadOnlyList<string> tokens, ConversationReferenceAlias alias)
    {
        var words = alias.NormalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var start = 0; start + words.Length <= tokens.Count; start++)
        {
            var matches = true;

            for (var offset = 0; offset < words.Length; offset++)
            {
                var token = tokens[start + offset];
                var expected = words[offset];

                // One leading Arabic conjunction may be attached to the first word of an alias
                // occurrence, so "والتانية" matches the existing alias "التانية" and "والديل…" matches
                // the existing full Current phrase. The conjunction is never stripped from later words,
                // it is never applied twice, and the allowlist itself stays unchanged.
                if (offset == 0 && IsConjunctionPrefixed(token, expected))
                {
                    continue;
                }

                if (!string.Equals(token, expected, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConjunctionPrefixed(string token, string expected) =>
        token.Length == expected.Length + 1
        && token[0] == 'و'
        && token.AsSpan(1).SequenceEqual(expected);

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
