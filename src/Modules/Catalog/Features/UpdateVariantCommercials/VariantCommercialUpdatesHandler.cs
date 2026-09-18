using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Features.UpdateVariantCommercials;

/// <summary>
/// The admin price, quantity and active-state use cases. Each one validates the requested change
/// against the persisted constraints and the audit requirement before anything is written.
/// </summary>
internal sealed class VariantCommercialUpdatesHandler(IVariantCommercialUpdateStore store)
    : ICatalogCommercialUpdates
{
    public Task<CommercialUpdateOutcome> UpdatePriceAsync(
        VariantPriceUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        CommercialUpdateRules.RequireVariantId(update.VariantId);
        CommercialUpdateRules.RequirePrice(update.Price);
        CommercialUpdateRules.RequireActor(update.ActorUserId);

        return store.UpdatePriceAsync(update, cancellationToken);
    }

    public Task<CommercialUpdateOutcome> UpdateQuantityAsync(
        VariantQuantityUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        CommercialUpdateRules.RequireVariantId(update.VariantId);
        CommercialUpdateRules.RequireQuantity(update.Quantity);
        CommercialUpdateRules.RequireActor(update.ActorUserId);

        return store.UpdateQuantityAsync(update, cancellationToken);
    }

    public Task<CommercialUpdateOutcome> UpdateActiveStateAsync(
        VariantActiveStateUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        CommercialUpdateRules.RequireVariantId(update.VariantId);
        CommercialUpdateRules.RequireActor(update.ActorUserId);

        return store.UpdateActiveStateAsync(update, cancellationToken);
    }
}
