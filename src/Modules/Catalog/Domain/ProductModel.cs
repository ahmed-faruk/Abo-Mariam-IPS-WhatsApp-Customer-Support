namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// A monitor model. Owns the technical specification facts that are never authored by the AI.
/// </summary>
public sealed class ProductModel
{
    public long Id { get; set; }

    public string ModelCode { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public decimal SizeInches { get; set; }

    public string PanelType { get; set; } = string.Empty;

    public int ResolutionWidth { get; set; }

    public int ResolutionHeight { get; set; }

    public int RefreshRate { get; set; }

    public string? Description { get; set; }

    public List<string> SearchTags { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ProductModelPort> Ports { get; set; } = [];

    public List<ProductVariant> Variants { get; set; } = [];
}
