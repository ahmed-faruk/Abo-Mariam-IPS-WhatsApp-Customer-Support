using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;

/// <summary>
/// The persistence port of the business-info read use case. The module-internal implementation owns
/// the query; the use case owns key validation.
/// </summary>
internal interface IBusinessInfoReader
{
    Task<BusinessInfoValue?> GetActiveByKeyAsync(string canonicalKey, CancellationToken cancellationToken);
}
