using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;

/// <summary>
/// The persistence port of the business-info read use case. The module-internal implementation owns
/// the query and returns the stored row whatever its active state; the use case owns key validation
/// and the customer-facing visibility rule.
/// </summary>
internal interface IBusinessInfoReader
{
    /// <summary>Returns the stored row of one canonical key, or null when that key has no row.</summary>
    Task<BusinessInfoValue?> GetByKeyAsync(string canonicalKey, CancellationToken cancellationToken);
}
