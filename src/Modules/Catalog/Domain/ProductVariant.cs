namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// A graded unit of a model. Price, quantity and warranty live here and are always reloaded
/// before the assistant renders a commercial reply.
/// </summary>
public sealed class ProductVariant
{
    public long Id { get; set; }

    public long ProductModelId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Grade { get; set; } = string.Empty;

    public decimal SellingPrice { get; set; }

    public int Quantity { get; set; }

    public int WarrantyDays { get; set; }

    public string? WarrantyNotes { get; set; }

    public string? CosmeticNotes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ProductModel? ProductModel { get; set; }
}
