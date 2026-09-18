using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Storefront;

/// <summary>
/// The visibility rule of the customer-facing read: an active stored answer is eligible to be
/// returned, an inactive one never is.
/// </summary>
public sealed class BusinessInfoVisibilityTests
{
    [Fact]
    public void An_active_stored_answer_is_customer_facing()
    {
        Assert.True(BusinessInfoVisibility.IsCustomerFacing(isActive: true));
    }

    [Fact]
    public void An_inactive_stored_answer_is_not_customer_facing()
    {
        Assert.False(BusinessInfoVisibility.IsCustomerFacing(isActive: false));
    }
}
