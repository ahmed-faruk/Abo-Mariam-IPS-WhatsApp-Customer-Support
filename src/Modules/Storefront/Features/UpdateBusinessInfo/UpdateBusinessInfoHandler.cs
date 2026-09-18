using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.UpdateBusinessInfo;

/// <summary>
/// The admin UpdateBusinessInfo use case. The key must be approved and the Arabic answer must not be
/// blank before anything is written. An approved key without a stored row is a NotFound result,
/// because this use case edits business information rather than authoring new keys.
/// </summary>
internal sealed class UpdateBusinessInfoHandler(IBusinessInfoUpdateStore store)
    : IStorefrontBusinessInfoUpdates
{
    public Task<BusinessInfoUpdateOutcome> UpdateAsync(
        BusinessInfoUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var change = new BusinessInfoChange(
            BusinessInfoKeys.RequireAllowed(update.Key),
            BusinessInfoAnswers.RequireAnswerAr(update.AnswerAr),
            BusinessInfoAnswers.NormalizeAnswerEn(update.AnswerEn),
            update.IsActive);

        return store.UpdateAsync(change, cancellationToken);
    }
}
