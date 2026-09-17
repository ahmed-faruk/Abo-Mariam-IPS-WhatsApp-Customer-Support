namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>A port offered by a monitor model, with how many of that port type are present.</summary>
public sealed class ProductModelPort
{
    public long Id { get; set; }

    public long ProductModelId { get; set; }

    public string PortType { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    public ProductModel? ProductModel { get; set; }
}
