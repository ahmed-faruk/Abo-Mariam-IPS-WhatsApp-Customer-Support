namespace WhatsAppMonitorAssistant.Modules.Storefront.Domain;

/// <summary>
/// The customer-facing visibility rule of stored business information: only an active row may answer a
/// customer. It is one domain decision rather than a query detail, so every customer-facing read path
/// applies the same rule and the rule itself stays unit-testable.
/// </summary>
public static class BusinessInfoVisibility
{
    /// <summary>
    /// True when the stored row is active, and therefore eligible to be returned to a customer by key.
    /// An inactive row is stored but unavailable until an admin activates it again.
    /// </summary>
    public static bool IsCustomerFacing(bool isActive) => isActive;
}
