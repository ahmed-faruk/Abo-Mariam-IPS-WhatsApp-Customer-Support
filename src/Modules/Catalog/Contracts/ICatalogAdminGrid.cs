namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// The Admin Lite catalogue grid of Issue #14: every variant with the commercial values an operator
/// edits, including out-of-stock and inactive ones. It only reads; edits go through
/// <see cref="ICatalogCommercialUpdates"/>.
/// </summary>
public interface ICatalogAdminGrid
{
    /// <summary>Returns at most 500 variants, ordered by model code, grade and variant id.</summary>
    Task<IReadOnlyList<CatalogGridRow>> ListVariantsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One variant row of the Admin Lite catalogue grid.</summary>
public sealed record CatalogGridRow(
    long ModelId,
    string ModelCode,
    string DisplayName,
    long VariantId,
    string Sku,
    string Grade,
    decimal Price,
    int Quantity,
    bool IsActive);
