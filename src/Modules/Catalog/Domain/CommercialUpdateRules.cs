namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// Input rules for the admin commercial updates. They mirror the persisted constraints
/// (<c>ck_variant_price</c>, <c>ck_variant_quantity</c>) so an invalid change is rejected before it
/// reaches the database.
/// </summary>
public static class CommercialUpdateRules
{
    public static void RequireVariantId(long variantId)
    {
        if (variantId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variantId), variantId, "The variant id must be positive.");
        }
    }

    /// <summary>Mirrors <c>ck_variant_price</c>: a stored price is never negative.</summary>
    public static void RequirePrice(decimal price)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "The selling price must not be negative.");
        }
    }

    /// <summary>Mirrors <c>ck_variant_quantity</c>: a stored quantity is never negative.</summary>
    public static void RequireQuantity(int quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "The quantity must not be negative.");
        }
    }

    /// <summary>Every commercial change is audited, so the acting admin must be identifiable.</summary>
    public static void RequireActor(string? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A commercial update requires the acting user id.", nameof(actorUserId));
        }
    }
}
