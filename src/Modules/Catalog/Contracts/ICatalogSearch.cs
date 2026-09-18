namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// Structured and lexical product search over the authoritative catalogue.
/// </summary>
public interface ICatalogSearch
{
    /// <summary>
    /// Returns the eligible models that satisfy every hard filter of <paramref name="query"/>, at
    /// most one recommendation per model, ordered by the documented deterministic ranking.
    /// </summary>
    /// <remarks>
    /// A model is eligible only when the model is active, the recommended variant is active and in
    /// stock, all required ports are offered, and the price satisfies the budget. A hard budget is a
    /// ceiling: no returned price is ever above it.
    /// </remarks>
    Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up one model by its exact model code, ignoring case and surrounding whitespace.
    /// Returns null when the code is unknown or when the model currently has no eligible variant,
    /// so a caller can never quote an exact code that is not actually available.
    /// </summary>
    Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default);
}
