namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// Catalogue change audit row. Price, quantity and activation changes are written in the same
/// module transaction as the change itself.
/// </summary>
public sealed class CatalogAuditLog
{
    public long Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public long? EntityId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? OldJson { get; set; }

    public string? NewJson { get; set; }

    public string? UserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
