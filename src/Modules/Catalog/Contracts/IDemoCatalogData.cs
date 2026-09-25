namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// The controlled-demo catalogue seed of docs/TECHNICAL.md section 36.5. It only ever inserts: a model
/// is matched by its exact model code and a variant by its exact SKU, and an existing row is never
/// updated, so a reseed cannot silently overwrite a value an operator changed. Restoring commercial
/// values is the job of the audited <see cref="ICatalogCommercialUpdates"/> contract. Only the demo
/// operator tooling registers this contract; the production composition root never does.
/// </summary>
public interface IDemoCatalogData
{
    /// <summary>
    /// Inserts every missing model, with its ports, and every missing variant in one transaction, and
    /// returns the stored variant id of every seeded SKU.
    /// </summary>
    Task<IReadOnlyDictionary<string, long>> EnsureSeedAsync(
        IReadOnlyList<DemoModelSeed> models,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the stored variant id of each given SKU that exists. It never writes.</summary>
    Task<IReadOnlyDictionary<string, long>> FindVariantIdsAsync(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken = default);
}

/// <summary>One demo monitor model with its ports and graded variants.</summary>
public sealed record DemoModelSeed(
    string ModelCode,
    string Brand,
    string Model,
    string DisplayName,
    decimal SizeInches,
    string PanelType,
    int ResolutionWidth,
    int ResolutionHeight,
    int RefreshRate,
    string? Description,
    IReadOnlyList<string> SearchTags,
    IReadOnlyList<DemoPortSeed> Ports,
    IReadOnlyList<DemoVariantSeed> Variants);

/// <summary>One port type of a demo model and how many of it the model offers.</summary>
public sealed record DemoPortSeed(string PortType, int Count);

/// <summary>One graded demo variant with its initial commercial values.</summary>
public sealed record DemoVariantSeed(
    string Sku,
    string Grade,
    decimal SellingPrice,
    int Quantity,
    int WarrantyDays,
    string? WarrantyNotes,
    string? CosmeticNotes);
