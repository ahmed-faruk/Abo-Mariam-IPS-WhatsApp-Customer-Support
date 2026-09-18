using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;

/// <summary>
/// The GetBusinessInfo use case. Only an approved key is looked up and only an active stored row is
/// returned, so a customer-facing caller can never be answered with a disabled or invented value.
/// </summary>
internal sealed class GetBusinessInfoHandler(IBusinessInfoReader reader) : IStorefrontBusinessInfo
{
    public Task<BusinessInfoValue?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var canonicalKey = BusinessInfoKeys.RequireAllowed(key);

        return reader.GetActiveByKeyAsync(canonicalKey, cancellationToken);
    }
}
