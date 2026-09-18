namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The search policy bounds docs/TECHNICAL.md section 10 states: one search returns at most twenty
/// recommendations (<c>LIMIT 20</c>). The configured maximum and its validation share this single
/// definition, so no deployment can widen the documented bound.
/// </summary>
public static class CatalogSearchPolicy
{
    /// <summary>The largest result set any caller can obtain.</summary>
    public const int MaximumResults = 20;
}
