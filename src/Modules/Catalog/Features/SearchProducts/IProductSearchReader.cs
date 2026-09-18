using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

/// <summary>
/// The persistence port of the search use case. The module-internal implementation owns the
/// PostgreSQL statement; the use case only owns normalization, budget resolution and bounding.
/// </summary>
internal interface IProductSearchReader
{
    Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        CatalogSearchCriteria criteria,
        CancellationToken cancellationToken);
}
