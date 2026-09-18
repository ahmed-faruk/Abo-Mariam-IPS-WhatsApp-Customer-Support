using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.GetProductDetails;

/// <summary>
/// The persistence port of the product details use case. The module-internal implementation owns the
/// queries; the use case owns argument validation and the availability rule.
/// </summary>
internal interface IProductDetailsReader
{
    Task<ProductDetails?> GetDetailsAsync(long productModelId, CancellationToken cancellationToken);

    Task<ProductRecommendation?> GetVariantFactsAsync(long productVariantId, CancellationToken cancellationToken);
}
