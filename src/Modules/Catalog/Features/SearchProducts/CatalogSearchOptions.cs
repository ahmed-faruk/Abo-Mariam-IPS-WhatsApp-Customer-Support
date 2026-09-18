namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

/// <summary>
/// Catalogue search policy. The defaults follow docs/TECHNICAL.md section 10, which searches with a
/// size tolerance and returns at most twenty recommendations.
/// </summary>
public sealed class CatalogSearchOptions
{
    /// <summary>
    /// How far a stored size may differ from the requested one. Real 23.8 inch panels are sold as 24
    /// inch monitors, so an exact match would hide them.
    /// </summary>
    public decimal SizeToleranceInches { get; set; } = 0.5m;

    /// <summary>
    /// How far above a soft budget target a result may be, as a fraction of that target. A hard
    /// budget ignores this value completely, because a hard ceiling is never widened.
    /// </summary>
    public decimal SoftBudgetTolerance { get; set; } = 0.15m;

    /// <summary>The largest result set any caller can obtain, so a search is always bounded.</summary>
    public int MaxResults { get; set; } = 20;
}
