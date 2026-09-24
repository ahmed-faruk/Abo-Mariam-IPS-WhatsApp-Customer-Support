using System.Globalization;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

/// <summary>
/// The bounded deterministic explicit-fact normalization of docs/TECHNICAL.md section 8.5. It runs
/// only after a model reply already satisfied the structured contract, and it may only enforce facts
/// the customer explicitly stated in the current text: an explicit brand alias, an explicit panel
/// token, a standalone monitor size with positive evidence, the documented budget phrase families
/// bound structurally to their number, and explicit shop-hours questions. Anything ambiguous is left
/// exactly as the model returned it, so the model stays the interpreter and this class stays a small
/// guardrail: it never infers a commercial fact, never resolves a reference and never rewrites a
/// non-success result.
/// </summary>
internal static class NluDeterministicNormalizer
{
    private const string CanonicalDellBrand = "Dell";

    // TECHNICAL section 6.1 ck_model_size: size_inches BETWEEN 10 AND 60.
    private const decimal MinMonitorSizeInches = 10m;
    private const decimal MaxMonitorSizeInches = 60m;

    private static readonly (NluBudgetType Type, string[] Tokens)[] BudgetPhrases =
    [
        (NluBudgetType.Soft, ["في", "حدود"]),
        (NluBudgetType.Soft, ["حوالي"]),
        (NluBudgetType.Hard, ["مش", "عايز", "اعدي"]),
        (NluBudgetType.Hard, ["بحد", "اقصي"]),
        (NluBudgetType.Hard, ["اقصي", "حاجه"]),
        (NluBudgetType.Hard, ["مايزدش", "عن"]),
    ];

    private static readonly HashSet<string> CurrencyUnits = new(StringComparer.Ordinal)
    {
        "جنيه", "ج", "جم", "egp", "le", "pound", "pounds",
    };

    private static readonly HashSet<string> PriceMagnitudeUnits = new(StringComparer.Ordinal)
    {
        "الف",
    };

    private static readonly HashSet<string> AlternativeMarkers = new(StringComparer.Ordinal)
    {
        "او", "ولا", "or",
    };

    private static readonly HashSet<string> SizeUnits = new(StringComparer.Ordinal)
    {
        "بوصه", "بوصات", "انش", "inch", "inches",
    };

    private static readonly HashSet<string> RefreshUnits = new(StringComparer.Ordinal)
    {
        "hz", "هرتز", "هيرتز", "fps",
    };

    private static readonly HashSet<string> DurationUnits = new(StringComparer.Ordinal)
    {
        "شهر", "شهور", "اشهر", "سنه", "سنين", "سنتين", "يوم", "ايام", "ساعه", "ساعات",
        "دقيقه", "دقايق", "اسبوع", "اسابيع",
        "month", "months", "year", "years", "day", "days", "hour", "hours",
    };

    private static readonly HashSet<string> QuantityUnits = new(StringComparer.Ordinal)
    {
        "قطعه", "قطع", "حته", "حتت", "وحده", "وحدات", "شاشات", "pcs", "pieces", "units",
    };

    private static readonly HashSet<string> GenericBudgetWords = new(StringComparer.Ordinal)
    {
        "عايز", "عاوز", "محتاج", "حاجه", "ميزانيتي", "شاشه", "monitor", "screen",
    };

    private static readonly HashSet<string> MonitorNouns = new(StringComparer.Ordinal)
    {
        "شاشه", "monitor", "screen",
    };

    private static readonly string[][] WorkingHoursPatterns =
    [
        ["مواعيدكم"],
        ["مواعيد", "العمل"],
        ["مواعيد", "الشغل"],
        ["بتفتحوا", "امتي"],
        ["فاتحين", "امتي"],
    ];

    /// <summary>
    /// Returns the interpretation with the facts of <paramref name="message"/> enforced, or the
    /// interpretation unchanged when the text states nothing this guardrail may act on.
    /// </summary>
    internal static NluInterpretation Normalize(string message, NluInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(interpretation);

        var tokens = Tokenize(message);
        var brand = ExplicitBrand(tokens, interpretation);
        var panel = ExplicitPanel(tokens);
        var (phraseBoundIndices, budget) = ExplicitBudget(tokens);
        var size = ExplicitSize(tokens, phraseBoundIndices, budget);
        var workingHours = ExplicitWorkingHours(tokens);

        var normalized = interpretation;

        if (brand is not null)
        {
            normalized = normalized with { Brand = brand };
        }

        if (panel is not null)
        {
            normalized = normalized with { Panel = panel };
        }

        // The budget postcondition keeps the normalizer local: it never produces a shape the
        // structured contract's own budget rules would reject.
        if (budget is { } established
            && NluBudgetRules.Validate(established.Type, established.Value, null, null).Count == 0)
        {
            normalized = normalized with
            {
                BudgetType = established.Type,
                BudgetTarget = established.Value,
                BudgetMin = null,
                BudgetMax = null,
            };
        }

        if (size is { } inches)
        {
            normalized = normalized with { SizeInches = inches.Value };
        }

        if (ExplicitIntent(tokens, normalized, brand, panel, size?.Value, budget, workingHours) is { } intent)
        {
            normalized = normalized with { Intent = intent };
        }

        return normalized;
    }

    private static bool IsNegatedOccurrence(IReadOnlyList<string> tokens, int index)
    {
        if (index > 0
            && tokens[index - 1] is "لا" or "not" or "no")
        {
            return true;
        }

        return index >= 2
            && tokens[index - 2] == "مش"
            && tokens[index - 1] is "عايز" or "عاوز" or "محتاج";
    }

    private static bool IsAlternativeOccurrence(IReadOnlyList<string> tokens, int index) =>
        (index > 0 && AlternativeMarkers.Contains(tokens[index - 1]))
        || (index + 1 < tokens.Count && AlternativeMarkers.Contains(tokens[index + 1]));

    /// <summary>
    /// The explicit Dell alias family of Issue #32. Whole tokens only: <c>الديل</c> and <c>بديل</c>
    /// are different tokens and stay unmatched.
    /// </summary>
    private static string? ExplicitBrand(IReadOnlyList<string> tokens, NluInterpretation interpretation)
    {
        var dellIndices = Enumerable.Range(0, tokens.Count)
            .Where(index => IsDellAlias(tokens[index]))
            .ToList();

        if (dellIndices.Count == 0)
        {
            return null;
        }

        if (dellIndices.Any(index =>
                IsNegatedOccurrence(tokens, index)
                || IsAlternativeOccurrence(tokens, index)))
        {
            return null;
        }

        if (interpretation.Brand is { } modelBrand
            && !string.Equals(modelBrand, CanonicalDellBrand, StringComparison.OrdinalIgnoreCase)
            && tokens.Contains(NormalizeWord(modelBrand), StringComparer.Ordinal))
        {
            return null;
        }

        return CanonicalDellBrand;
    }

    private static bool IsDellAlias(string token) => token is "dell" or "ديل";

    /// <summary>
    /// The explicit panel vocabulary of Issue #32. Whole tokens only, canonical uppercase, and only
    /// when the text names exactly one distinct panel: two panel tokens state no choice at all, and
    /// a use case is not a panel.
    /// </summary>
    private static string? ExplicitPanel(IReadOnlyList<string> tokens)
    {
        var occurrences = tokens
            .Select((token, index) => (Panel: CanonicalPanel(token), Index: index))
            .Where(occurrence => occurrence.Panel is not null)
            .ToList();

        if (occurrences.Any(occurrence => IsNegatedOccurrence(tokens, occurrence.Index)))
        {
            return null;
        }

        var panels = occurrences
            .Select(occurrence => occurrence.Panel!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return panels.Count == 1 ? panels[0] : null;
    }

    private static string? CanonicalPanel(string token) => token switch
    {
        "ips" => "IPS",
        "tn" => "TN",
        "va" => "VA",
        "oled" => "OLED",
        _ => null,
    };

    /// <summary>
    /// The documented Soft/Hard phrase families of Issue #32, bound structurally to the one numeric
    /// token that follows the matched phrase. The phrase positions are recorded whether or not a
    /// budget is established, so a number the phrase owns can never be claimed by the size rule.
    /// </summary>
    private static (IReadOnlySet<int> PhraseBoundIndices, BudgetFact? Budget) ExplicitBudget(
        IReadOnlyList<string> tokens)
    {
        var matches = new List<BudgetPhrase>();

        foreach (var (type, phrase) in BudgetPhrases)
        {
            for (var start = 0; start + phrase.Length <= tokens.Count; start++)
            {
                if (PhraseMatches(tokens, start, phrase))
                {
                    matches.Add(new BudgetPhrase(type, start, phrase.Length));
                }
            }
        }

        var phraseBoundIndices = new HashSet<int>();

        foreach (var match in matches)
        {
            for (var index = match.Start; index < match.Start + match.Length; index++)
            {
                phraseBoundIndices.Add(index);
            }

            if (match.Start + match.Length < tokens.Count)
            {
                phraseBoundIndices.Add(match.Start + match.Length);
            }
        }

        // Two phrase occurrences (or two families) state no single budget.
        if (matches.Count != 1)
        {
            return (phraseBoundIndices, null);
        }

        var matched = matches[0];
        var numberIndex = matched.Start + matched.Length;

        if (numberIndex >= tokens.Count
            || !TryInteger(tokens[numberIndex], out var value)
            || value <= 0)
        {
            return (phraseBoundIndices, null);
        }

        var next = numberIndex + 1 < tokens.Count ? tokens[numberIndex + 1] : null;

        // A number owned by a size, refresh, duration or quantity unit is not a budget.
        if (next is not null
            && (SizeUnits.Contains(next)
                || RefreshUnits.Contains(next)
                || DurationUnits.Contains(next)
                || IsQuantityToken(next)))
        {
            return (phraseBoundIndices, null);
        }

        // A two-digit number is normally a screen size, so it needs the customer's own currency
        // marker before it can be read as a budget; otherwise neither rule claims it.
        if (value is >= 10 and <= 60 && (next is null || !CurrencyUnits.Contains(next)))
        {
            return (phraseBoundIndices, null);
        }

        return (
            phraseBoundIndices,
            new BudgetFact(matched.Type, value, matched.Start, matched.Length, numberIndex));
    }

    private static bool PhraseMatches(
        IReadOnlyList<string> tokens,
        int start,
        string[] phrase,
        bool allowConjunction = true)
    {
        for (var offset = 0; offset < phrase.Length; offset++)
        {
            var token = tokens[start + offset];
            var expected = phrase[offset];

            // The first phrase token may carry one attached conjunction, so "ومش عايز أعدي" matches.
            if (offset == 0 && allowConjunction && IsConjunctionPrefixed(token, expected))
            {
                continue;
            }

            if (!string.Equals(token, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConjunctionPrefixed(string token, string expected) =>
        token.Length == expected.Length + 1
        && token[0] == 'و'
        && token.AsSpan(1).SequenceEqual(expected);

    private static bool IsQuantityToken(string token) =>
        QuantityUnits.Contains(token) || string.Equals(token, "شاشه", StringComparison.Ordinal);

    /// <summary>
    /// The standalone monitor size of Issue #32. A number is a size only when it is inside the
    /// documented size range, no other role owns it (the budget phrase, a resolution, a refresh rate,
    /// a model code, a warranty, opening hours, a quantity or a price), and the text carries positive
    /// evidence that it is a size. Two distinct candidates state no single size and keep the model's.
    /// </summary>
    private static SizeFact? ExplicitSize(
        IReadOnlyList<string> tokens,
        IReadOnlySet<int> phraseBoundIndices,
        BudgetFact? budget)
    {
        var candidates = new List<SizeFact>();

        for (var index = 0; index < tokens.Count; index++)
        {
            if (phraseBoundIndices.Contains(index) || budget?.NumberIndex == index)
            {
                continue;
            }

            if (!TryInteger(tokens[index], out var number)
                || number < MinMonitorSizeInches
                || number > MaxMonitorSizeInches)
            {
                continue;
            }

            var previous = index > 0 ? tokens[index - 1] : null;
            var next = index + 1 < tokens.Count ? tokens[index + 1] : null;

            if ((next is not null
                    && (CurrencyUnits.Contains(next)
                        || PriceMagnitudeUnits.Contains(next)
                        || RefreshUnits.Contains(next)
                        || DurationUnits.Contains(next)
                        || IsQuantityToken(next)))
                || (previous is not null && IsPriceContext(previous)))
            {
                continue;
            }

            if (HasSizeEvidence(previous, next))
            {
                candidates.Add(new SizeFact(number, index));
            }
        }

        var distinct = candidates.Select(candidate => candidate.Value).Distinct().ToList();

        return distinct.Count == 1
            ? candidates.First(candidate => candidate.Value == distinct[0])
            : null;
    }

    private static bool HasSizeEvidence(string? previous, string? next) =>
        (next is not null && SizeUnits.Contains(next))
        || (previous is not null && (IsDellAlias(previous) || MonitorNouns.Contains(previous)))
        || (previous is not null && CanonicalPanel(previous) is not null)
        || (next is not null && CanonicalPanel(next) is not null);

    private static bool IsPriceContext(string token) =>
        token.StartsWith("سعر", StringComparison.Ordinal)
        || token.EndsWith("سعر", StringComparison.Ordinal)
        || token is "بكام" or "ضمان" or "الضمان" or "بضمان" or "ريفرش" or "refresh";

    /// <summary>
    /// The explicit shop-hours questions of Issue #32. Deliberately narrow: an appointment, a delivery
    /// or a reservation that merely shares the word stem is not a shop-hours question.
    /// </summary>
    private static bool ExplicitWorkingHours(IReadOnlyList<string> tokens)
    {
        foreach (var pattern in WorkingHoursPatterns)
        {
            for (var start = 0; start + pattern.Length <= tokens.Count; start++)
            {
                if (PhraseMatches(tokens, start, pattern, allowConjunction: false))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The intent correction of Issue #32, decided from the customer's text and the established facts
    /// only. A message that is nothing but a stated budget search becomes <see cref="NluIntent.ProductSearch"/>
    /// whatever the model returned; any token the guardrail cannot explain — a price word, a model
    /// code, an availability or handoff request, a shop-hours question — keeps the model intent.
    /// </summary>
    private static NluIntent? ExplicitIntent(
        IReadOnlyList<string> tokens,
        NluInterpretation interpretation,
        string? brand,
        string? panel,
        decimal? sizeInches,
        BudgetFact? budget,
        bool workingHours)
    {
        if (budget is { } established
            && IsUnambiguousBudgetTurn(tokens, brand, panel, sizeInches, established))
        {
            return NluIntent.ProductSearch;
        }

        // A shop-hours question is answered as BusinessInfo only when the same message states no
        // product fact and does not already ask for a human.
        if (workingHours
            && interpretation.Intent != NluIntent.HumanHandoff
            && !HasProductSpecificField(interpretation))
        {
            return NluIntent.BusinessInfo;
        }

        // A filter-only catalogue request: the model answered with catalogue constraints and named no
        // specific product, so the turn refines or starts a search instead of asking about one item.
        if (!tokens.Any(token => AlternativeMarkers.Contains(token))
            && IsFilterOnlySearchRefinement(interpretation))
        {
            return NluIntent.ProductSearch;
        }

        return null;
    }

    /// <summary>
    /// True when a <see cref="NluIntent.ProductDetails"/> result actually states only catalogue
    /// search/filter constraints and names no specific product. Purely structural: a model code or any
    /// reference names one item, and a result with no constraint at all proves no refinement, so both
    /// keep the model's intent.
    /// </summary>
    private static bool IsFilterOnlySearchRefinement(NluInterpretation interpretation) =>
        interpretation.Intent == NluIntent.ProductDetails
        && string.IsNullOrWhiteSpace(interpretation.ModelCode)
        && string.IsNullOrWhiteSpace(interpretation.Reference)
        && HasCatalogueFilter(interpretation);

    /// <summary>The catalogue constraints of the frozen structured contract that describe a search.</summary>
    private static bool HasCatalogueFilter(NluInterpretation interpretation) =>
        !string.IsNullOrWhiteSpace(interpretation.Brand)
        || interpretation.SizeInches is not null
        || !string.IsNullOrWhiteSpace(interpretation.Panel)
        || !string.IsNullOrWhiteSpace(interpretation.Resolution)
        || interpretation.MinRefreshRate is not null
        || interpretation.RequiredPorts.Count > 0
        || interpretation.Grades.Count > 0
        || !string.IsNullOrWhiteSpace(interpretation.UseCase)
        || (interpretation.BudgetType switch
        {
            NluBudgetType.Soft or NluBudgetType.Hard => interpretation.BudgetTarget is not null,
            NluBudgetType.Range =>
                interpretation.BudgetMin is not null || interpretation.BudgetMax is not null,
            _ => false,
        });

    private static bool HasProductSpecificField(NluInterpretation interpretation) =>
        !string.IsNullOrWhiteSpace(interpretation.ModelCode)
        || !string.IsNullOrWhiteSpace(interpretation.Reference)
        || HasCatalogueFilter(interpretation);

    private static bool IsUnambiguousBudgetTurn(
        IReadOnlyList<string> tokens,
        string? brand,
        string? panel,
        decimal? sizeInches,
        BudgetFact budget)
    {
        var explained = new HashSet<int>();

        for (var index = budget.Start; index < budget.Start + budget.Length; index++)
        {
            explained.Add(index);
        }

        explained.Add(budget.NumberIndex);

        if (budget.NumberIndex + 1 < tokens.Count
            && CurrencyUnits.Contains(tokens[budget.NumberIndex + 1]))
        {
            explained.Add(budget.NumberIndex + 1);
        }

        if (sizeInches is { } inches)
        {
            foreach (var index in SizeTokenIndices(tokens, inches))
            {
                explained.Add(index);

                if (index + 1 < tokens.Count && SizeUnits.Contains(tokens[index + 1]))
                {
                    explained.Add(index + 1);
                }
            }
        }

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if ((brand is not null && IsDellAlias(token))
                || (panel is not null && CanonicalPanel(token) is not null)
                || GenericBudgetWords.Contains(token))
            {
                explained.Add(index);
            }
        }

        for (var index = 0; index < tokens.Count; index++)
        {
            if (!explained.Contains(index))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<int> SizeTokenIndices(IReadOnlyList<string> tokens, decimal inches)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (TryInteger(tokens[index], out var value) && value == inches)
            {
                yield return index;
            }
        }
    }

    private static bool TryInteger(string token, out long value)
    {
        value = 0;

        if (token.Length is 0 or > 9)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct BudgetPhrase(NluBudgetType Type, int Start, int Length);

    private readonly record struct BudgetFact(
        NluBudgetType Type,
        decimal Value,
        int Start,
        int Length,
        int NumberIndex);

    private readonly record struct SizeFact(decimal Value, int Index);

    /// <summary>
    /// The comparison form of one message: Arabic diacritics and tatweel removed, the common letter
    /// folds applied, Arabic-Indic digits folded to ASCII, lowercased, and split into tokens on
    /// whitespace and punctuation. A decimal separator or thousands separator between two digits
    /// stays inside its token, so <c>23.8</c> and <c>3,000</c> can never read as integers.
    /// </summary>
    private static IReadOnlyList<string> Tokenize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return [];
        }

        var folded = Fold(message);
        var tokens = new List<string>();
        var current = new StringBuilder();

        for (var index = 0; index < folded.Length; index++)
        {
            var character = folded[index];

            if (IsDigitSeparator(character)
                && current.Length > 0
                && char.IsAsciiDigit(current[^1])
                && index + 1 < folded.Length
                && char.IsAsciiDigit(folded[index + 1]))
            {
                current.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                Flush(tokens, current);
                continue;
            }

            current.Append(character);
        }

        Flush(tokens, current);

        return tokens;
    }

    private static bool IsDigitSeparator(char character) => character is '.' or ',' or '\u066B' or '\u066C';

    private static void Flush(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            switch (character)
            {
                case >= '\u064B' and <= '\u065F':
                case '\u0670':
                case '\u0640':
                    continue;

                case 'أ' or 'إ' or 'آ' or 'ٱ':
                    builder.Append('ا');
                    continue;

                case 'ى':
                    builder.Append('ي');
                    continue;

                case 'ة':
                    builder.Append('ه');
                    continue;

                case >= '\u0660' and <= '\u0669':
                    builder.Append((char)('0' + (character - '\u0660')));
                    continue;

                default:
                    builder.Append(char.ToLowerInvariant(character));
                    continue;
            }
        }

        return builder.ToString();
    }

    private static string NormalizeWord(string text) => Fold(text).Trim();
}
