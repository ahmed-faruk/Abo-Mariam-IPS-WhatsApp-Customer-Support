using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.UpdateVariantCommercials;

/// <summary>
/// The persistence port of the admin commercial updates. The module-internal implementation owns the
/// transaction that changes the stored value and writes its audit row together.
/// </summary>
internal interface IVariantCommercialUpdateStore
{
    Task<CommercialUpdateOutcome> UpdatePriceAsync(
        VariantPriceUpdate update,
        CancellationToken cancellationToken);

    Task<CommercialUpdateOutcome> UpdateQuantityAsync(
        VariantQuantityUpdate update,
        CancellationToken cancellationToken);

    Task<CommercialUpdateOutcome> UpdateActiveStateAsync(
        VariantActiveStateUpdate update,
        CancellationToken cancellationToken);
}
