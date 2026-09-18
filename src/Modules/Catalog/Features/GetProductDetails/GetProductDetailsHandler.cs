using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.GetProductDetails;

/// <summary>
/// The GetProductDetails use case, and the current-facts reload the renderer uses for a shortlisted
/// variant. Both read the stored facts directly, so a reply can never be built from stale values.
/// </summary>
internal sealed class GetProductDetailsHandler(IProductDetailsReader reader) : ICatalogProductDetails
{
    public Task<ProductDetails?> GetDetailsAsync(
        long productModelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productModelId);

        return reader.GetDetailsAsync(productModelId, cancellationToken);
    }

    public Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productVariantId);

        return reader.GetVariantFactsAsync(productVariantId, cancellationToken);
    }
}
