namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The grades allowed by the <c>ck_variant_grade</c> constraint, in the priority order of
/// docs/PLAN.md ADR-M07: grade A is preferred over B, and B over C.
/// </summary>
public static class Grades
{
    public const string A = "A";

    public const string B = "B";

    public const string C = "C";

    public static IReadOnlyList<string> Allowed { get; } = [A, B, C];

    /// <summary>
    /// Returns the stored spelling when the value matches a known grade, and the normalized value
    /// otherwise so an unknown grade honestly matches nothing.
    /// </summary>
    public static string? Canonicalize(string? value)
    {
        var normalized = CatalogText.NormalizeOrNull(value);

        if (normalized is null)
        {
            return null;
        }

        foreach (var allowed in Allowed)
        {
            if (string.Equals(allowed, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return allowed;
            }
        }

        return normalized;
    }

    /// <summary>Canonicalizes the accepted grades, dropping blanks and repeats but keeping the order.</summary>
    public static IReadOnlyList<string> CanonicalizeAll(IEnumerable<string>? values) =>
        [.. Distinct(values, Canonicalize)];

    private static IEnumerable<string> Distinct(IEnumerable<string>? values, Func<string?, string?> canonicalize)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var canonical = canonicalize(value);

            if (canonical is not null && seen.Add(canonical))
            {
                yield return canonical;
            }
        }
    }
}
