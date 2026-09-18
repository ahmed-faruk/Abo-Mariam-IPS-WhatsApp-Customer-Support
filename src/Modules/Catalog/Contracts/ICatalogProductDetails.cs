namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// The authoritative read side of the catalogue. Every call reads PostgreSQL directly, so the
/// caller always sees the current price, quantity and active state.
/// </summary>
public interface ICatalogProductDetails
{
    /// <summary>Returns the full stored facts of one model, or null when it does not exist.</summary>
    Task<ProductDetails?> GetDetailsAsync(
        long productModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the current facts of one variant in the shape of a recommendation. This is the
    /// revalidation step of docs/TECHNICAL.md section 12 that runs immediately before a reply is
    /// rendered, so a stale shortlist can never quote a price, a quantity or an active state that
    /// changed since the search.
    /// </summary>
    Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
