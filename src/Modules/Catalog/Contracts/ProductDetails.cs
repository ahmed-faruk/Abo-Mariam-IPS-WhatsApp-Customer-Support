namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// The authoritative facts of one model and every one of its variants, read from PostgreSQL.
/// </summary>
public sealed record ProductDetails
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

    public string? Description { get; init; }

    /// <summary>The curated use-case tags of the model.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool IsActive { get; init; }

    public IReadOnlyList<ProductPortFact> Ports { get; init; } = [];

    /// <summary>
    /// Every stored variant, active or not, with its current price, quantity and availability, so a
    /// caller can see exactly why a variant is not recommendable.
    /// </summary>
    public IReadOnlyList<ProductVariantDetails> Variants { get; init; } = [];
}

/// <summary>A port type a model offers, and how many of it.</summary>
public sealed record ProductPortFact(string PortType, int Count);

/// <summary>The current commercial facts of one variant.</summary>
public sealed record ProductVariantDetails
{
    public long VariantId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Grade { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public int Quantity { get; init; }

    public int WarrantyDays { get; init; }

    public string? WarrantyNotes { get; init; }

    public string? CosmeticNotes { get; init; }

    public bool IsActive { get; init; }

    /// <summary>True when the model and this variant are active and the quantity is above zero.</summary>
    public bool IsAvailable { get; init; }
}
