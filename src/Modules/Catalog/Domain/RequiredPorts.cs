namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// Required ports are a set, not a list: asking for HDMI and DisplayPort twice still means the model
/// must offer both types once.
/// </summary>
public static class RequiredPorts
{
    /// <summary>
    /// Normalizes the requested ports the same way the stored ones are compared: trimmed, collapsed,
    /// lowercased, blanks dropped and repeats removed.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? ports)
    {
        if (ports is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var port in ports)
        {
            var value = CatalogText.NormalizeOrNull(port);

            if (value is not null && seen.Add(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }
}
