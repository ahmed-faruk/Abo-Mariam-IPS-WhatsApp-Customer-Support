using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

/// <summary>
/// The SearchProducts use case: normalizes the caller query, resolves the budget and the bounded
/// limit, and delegates the eligibility, ranking and one-recommendation-per-model rules to the
/// PostgreSQL statement.
/// </summary>
internal sealed class SearchProductsHandler(
    IProductSearchReader reader,
    CatalogSearchOptions options) : ICatalogSearch
{
    public Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var criteria = CatalogSearchCriteriaFactory.From(
            query,
            options.SizeToleranceInches,
            options.SoftBudgetTolerance,
            options.MaxResults);

        return reader.SearchAsync(criteria, cancellationToken);
    }

    public async Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelCode);

        // The model-code filter is a hard filter, so an exact lookup is the documented search with
        // one requested result: it can never return a different code, and it never returns a model
        // whose variants are inactive or out of stock.
        var criteria = CatalogSearchCriteriaFactory.From(
            new ProductSearchQuery { ModelCode = modelCode, Limit = 1 },
            options.SizeToleranceInches,
            options.SoftBudgetTolerance,
            options.MaxResults);

        var recommendations = await reader.SearchAsync(criteria, cancellationToken);

        return recommendations.Count == 0 ? null : recommendations[0];
    }
}
