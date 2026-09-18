namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// One recommended model together with the single variant that represents it. Every commercial and
/// specification value is read from PostgreSQL at query time; the AI never authors any of them.
/// </summary>
/// <remarks>
/// A search returns at most one recommendation per model, so <see cref="VariantId"/> is the best
/// eligible variant of that model under the documented ranking. Callers keep the model and variant
/// ids and re-read the current facts through <see cref="ICatalogProductDetails.GetVariantFactsAsync"/>
/// immediately before rendering or sending, so a stale shortlist can never quote a stale price.
/// </remarks>
public sealed record ProductRecommendation
{
    public long ModelId { get; init; }

    public string ModelCode { get; init; } = string.Empty;

    public string Brand { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public decimal SizeInches { get; init; }

    public string PanelType { get; init; } = string.Empty;

    public int ResolutionWidth { get; init; }

    public int ResolutionHeight { get; init; }

    public int RefreshRate { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Ports { get; init; } = [];

    public long VariantId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Grade { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public int Quantity { get; init; }

    public int WarrantyDays { get; init; }

    public string? WarrantyNotes { get; init; }

    public string? CosmeticNotes { get; init; }

    /// <summary>True when the model and the variant are active and the variant has stock.</summary>
    public bool IsAvailable { get; init; }
}
