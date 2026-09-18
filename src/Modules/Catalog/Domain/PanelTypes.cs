namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The panel types allowed by the <c>ck_model_panel</c> constraint. A filter is mapped back to the
/// stored spelling, so <c>other</c> finds the stored <c>Other</c> instead of silently matching
/// nothing.
/// </summary>
public static class PanelTypes
{
    public const string Ips = "IPS";

    public const string Tn = "TN";

    public const string Va = "VA";

    public const string Oled = "OLED";

    public const string Other = "Other";

    public static IReadOnlyList<string> Allowed { get; } = [Ips, Tn, Va, Oled, Other];

    /// <summary>
    /// Returns the stored spelling when the filter matches a known panel type, and the normalized
    /// value otherwise so an unknown panel type honestly matches nothing.
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
}
