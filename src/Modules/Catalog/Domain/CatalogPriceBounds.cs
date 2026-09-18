namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The price range the catalogue storage itself defines. It is derived from the shape of the
/// <c>catalog.product_variant.selling_price</c> column, <c>numeric(12,2)</c> of docs/TECHNICAL.md
/// section 6.1: twelve digits with two decimals. It is a representation bound, not a sales policy,
/// and it exists so a resolved bound can saturate instead of overflowing.
/// </summary>
public static class CatalogPriceBounds
{
    /// <summary>The largest price <c>numeric(12,2)</c> can store.</summary>
    public const decimal MaxSellingPrice = 9_999_999_999.99m;
}
