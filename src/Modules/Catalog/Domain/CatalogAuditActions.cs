namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>The audited catalogue actions. The value is stored verbatim in the audit row.</summary>
public static class CatalogAuditActions
{
    public const string UpdatePrice = "UpdatePrice";

    public const string UpdateQuantity = "UpdateQuantity";

    public const string UpdateActiveState = "UpdateActiveState";
}
