using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;

/// <summary>
/// The GetBusinessInfo use case. Only an approved key is looked up, and the stored row is returned only
/// when the visibility rule says it is customer-facing, so a caller can never be answered with a
/// disabled or invented value.
/// </summary>
internal sealed class GetBusinessInfoHandler(IBusinessInfoReader reader) : IStorefrontBusinessInfo
{
    public async Task<BusinessInfoValue?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var canonicalKey = BusinessInfoKeys.RequireAllowed(key);
        var stored = await reader.GetByKeyAsync(canonicalKey, cancellationToken);

        // A disabled answer is not a customer-facing fact, so it reads exactly like a missing one.
        return stored is not null && BusinessInfoVisibility.IsCustomerFacing(stored.IsActive) ? stored : null;
    }
}
