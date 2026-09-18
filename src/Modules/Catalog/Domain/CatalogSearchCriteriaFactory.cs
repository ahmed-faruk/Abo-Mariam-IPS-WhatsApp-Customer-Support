using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// Builds the normalized criteria of a search. Values that could not constrain a catalogue honestly
/// (a size, a resolution or a refresh rate that is not above zero) are treated as absent instead of
/// matching nothing, and the requested limit is always clamped to the configured maximum and to the
/// search policy of docs/TECHNICAL.md section 10.
/// </summary>
public static class CatalogSearchCriteriaFactory
{
    public static CatalogSearchCriteria From(
        ProductSearchQuery query,
        decimal sizeToleranceInches,
        decimal softBudgetTolerance,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (sizeToleranceInches < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeToleranceInches),
                sizeToleranceInches,
                "The size tolerance must not be negative.");
        }

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "The result maximum must be positive.");
        }

        var budget = BudgetRules.Resolve(query.Budget, softBudgetTolerance);
        var size = PositiveOrNull(query.SizeInches);

        return new CatalogSearchCriteria
        {
            Text = CatalogText.NormalizeOrNull(query.Text),
            ModelCode = CatalogText.NormalizeOrNull(query.ModelCode),
            Brand = CatalogText.NormalizeOrNull(query.Brand),
            SizeInches = size,
            SizeToleranceInches = size is null ? 0 : sizeToleranceInches,
            PanelType = PanelTypes.Canonicalize(query.PanelType),
            MinResolutionWidth = PositiveOrNull(query.MinResolutionWidth),
            MinResolutionHeight = PositiveOrNull(query.MinResolutionHeight),
            MinRefreshRate = PositiveOrNull(query.MinRefreshRate),
            RequiredPorts = RequiredPorts.Normalize(query.RequiredPorts),
            Grades = Grades.CanonicalizeAll(query.Grades),
            BudgetMin = budget.Min,
            BudgetMax = budget.Max,
            BudgetTarget = query.Budget?.Target,
            UseCase = CatalogText.NormalizeOrNull(query.UseCase),
            Limit = BoundedLimit(query.Limit, maxResults),
        };
    }

    private static decimal? PositiveOrNull(decimal? value) => value is > 0 ? value : null;

    private static int? PositiveOrNull(int? value) => value is > 0 ? value : null;

    /// <summary>
    /// The configured maximum is validated against the policy bound, and it is clamped here as well so
    /// a host that skipped its own configuration validation still cannot run a wider search.
    /// </summary>
    private static int BoundedLimit(int? requested, int maxResults)
    {
        var maximum = Math.Min(maxResults, CatalogSearchPolicy.MaximumResults);

        return requested is null or <= 0 ? maximum : Math.Min(requested.Value, maximum);
    }
}
