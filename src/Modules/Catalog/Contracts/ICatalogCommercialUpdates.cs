namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// The admin write side of the catalogue. Each update changes one stored commercial fact and writes
/// its audit row in the same PostgreSQL transaction, so the catalogue can never change without a
/// matching audit entry.
/// </summary>
public interface ICatalogCommercialUpdates
{
    /// <summary>Sets the current selling price of a variant.</summary>
    Task<CommercialUpdateOutcome> UpdatePriceAsync(
        VariantPriceUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the current quantity of a variant.</summary>
    Task<CommercialUpdateOutcome> UpdateQuantityAsync(
        VariantQuantityUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates a variant.</summary>
    Task<CommercialUpdateOutcome> UpdateActiveStateAsync(
        VariantActiveStateUpdate update,
        CancellationToken cancellationToken = default);
}

/// <summary>A price change requested by an admin.</summary>
/// <param name="VariantId">The variant to change.</param>
/// <param name="Price">The new price. It must not be negative.</param>
/// <param name="ActorUserId">The admin identity recorded in the audit row.</param>
public sealed record VariantPriceUpdate(long VariantId, decimal Price, string ActorUserId);

/// <summary>A quantity change requested by an admin.</summary>
/// <param name="VariantId">The variant to change.</param>
/// <param name="Quantity">The new quantity. It must not be negative.</param>
/// <param name="ActorUserId">The admin identity recorded in the audit row.</param>
public sealed record VariantQuantityUpdate(long VariantId, int Quantity, string ActorUserId);

/// <summary>An active-state change requested by an admin.</summary>
/// <param name="VariantId">The variant to change.</param>
/// <param name="IsActive">The new active state.</param>
/// <param name="ActorUserId">The admin identity recorded in the audit row.</param>
public sealed record VariantActiveStateUpdate(long VariantId, bool IsActive, string ActorUserId);
