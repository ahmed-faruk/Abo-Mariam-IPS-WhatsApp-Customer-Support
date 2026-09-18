namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>
/// The customer-facing read side of the business information. Every call reads the current stored row,
/// so a caller always answers with the authoritative business policy instead of an invented one.
/// </summary>
public interface IStorefrontBusinessInfo
{
    /// <summary>
    /// Returns the current active stored value of one approved key, or null when that key has no active
    /// stored row. A key outside the allowlist is rejected instead of being looked up.
    /// </summary>
    Task<BusinessInfoValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
}
