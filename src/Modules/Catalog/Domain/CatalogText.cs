namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The one deterministic normalization the catalogue uses for every text comparison. Stored values
/// are never rewritten, so PostgreSQL applies the same canonical form with
/// <c>catalog.canonical_text(value)</c> to both sides of a comparison: trimming, collapsing whitespace
/// runs and case folding. That function, created by the corrective Catalog migration, is also the
/// expression of the unique model-code index, so a query can never be ambiguous.
/// </summary>
public static class CatalogText
{
    /// <summary>
    /// Trims, collapses internal whitespace runs to one space, and lowercases with the invariant
    /// culture, so "  P2419H " and "p2419h" normalize to the same value.
    /// </summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>Normalizes a value that may be absent. Blank input is no filter at all.</summary>
    public static string? NormalizeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
}
