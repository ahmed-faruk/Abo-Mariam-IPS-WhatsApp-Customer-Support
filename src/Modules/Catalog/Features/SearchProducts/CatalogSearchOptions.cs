using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

/// <summary>
/// Catalogue search policy. The result bound follows docs/TECHNICAL.md section 10, which limits a
/// search to twenty recommendations.
/// </summary>
/// <remarks>
/// Both tolerances are mandatory configuration. docs/PLAN.md UC-02 allows a configurable tolerance for
/// a soft budget, and docs/TECHNICAL.md section 10 searches with a size tolerance, but neither
/// document defines numeric values for them. The module therefore has no tolerance defaults at all:
/// leaving one unset fails configuration validation instead of silently applying an invented policy.
/// </remarks>
public sealed class CatalogSearchOptions
{
    /// <summary>
    /// The configuration section the composition root binds this policy from, so the tolerated
    /// distances are an explicit deployment decision rather than a module constant.
    /// </summary>
    public const string ConfigurationSectionName = "Catalog:Search";

    /// <summary>
    /// How far a stored size may differ from the requested one, in inches. Required. It must be a
    /// non-negative distance that can still discriminate, so it is bounded by the size range of the
    /// <c>ck_model_size</c> constraint.
    /// </summary>
    public decimal? SizeToleranceInches { get; set; }

    /// <summary>
    /// How far above a soft budget target a result may be, as a fraction of that target. Required.
    /// The resolved maximum is <c>target * (1 + SoftBudgetTolerance)</c>. A hard budget ignores this
    /// value completely, because a hard ceiling is never widened.
    /// </summary>
    public decimal? SoftBudgetTolerance { get; set; }

    /// <summary>The largest result set any caller can obtain, so a search is always bounded.</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>
    /// The validated size tolerance. Configuration validation rejects a missing value, so this is only
    /// a defensive guard for a host that was built without validating its own configuration.
    /// </summary>
    public decimal RequiredSizeToleranceInches => Required(
        SizeToleranceInches,
        nameof(SizeToleranceInches));

    /// <summary>
    /// The validated soft budget tolerance, resolved the same way as
    /// <see cref="RequiredSizeToleranceInches"/>.
    /// </summary>
    public decimal RequiredSoftBudgetTolerance => Required(
        SoftBudgetTolerance,
        nameof(SoftBudgetTolerance));

    private static decimal Required(decimal? value, string setting) =>
        value ?? throw new InvalidOperationException(
            $"The catalog search setting '{setting}' is not configured. Set it explicitly under "
            + $"'{ConfigurationSectionName}' (or the Catalog__Search__{setting} environment variable); "
            + "the module does not apply a tolerance the project baseline does not define.");
}
