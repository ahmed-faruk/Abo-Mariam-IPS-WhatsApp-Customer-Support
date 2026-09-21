using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Storefront;

/// <summary>
/// The canonical business-info key names are part of the cross-module contract, because a caller
/// outside Storefront such as the Conversations router has to name an approved key without
/// referencing a Storefront domain type. This test pins the contract surface to the stored values,
/// so the two can never drift.
/// </summary>
public sealed class BusinessInfoKeyNamesTests
{
    [Fact]
    public void The_contract_exposes_exactly_the_seven_approved_concepts()
    {
        Assert.Equal(
            [
                "WorkingHours",
                "Address",
                "Delivery",
                "PaymentMethods",
                "Warranty",
                "ContactPhone",
                "ReturnExchangePolicy",
            ],
            BusinessInfoKeyNames.All);
    }

    [Fact]
    public void Every_contract_name_is_the_value_the_module_stores()
    {
        Assert.Equal(
            [
                BusinessInfoKeyNames.WorkingHours,
                BusinessInfoKeyNames.Address,
                BusinessInfoKeyNames.Delivery,
                BusinessInfoKeyNames.PaymentMethods,
                BusinessInfoKeyNames.Warranty,
                BusinessInfoKeyNames.ContactPhone,
                BusinessInfoKeyNames.ReturnExchangePolicy,
            ],
            BusinessInfoKeyNames.All);

        Assert.Equal(BusinessInfoKeys.All, BusinessInfoKeyNames.All);
    }

    [Fact]
    public void The_contract_names_canonicalize_through_the_stored_allowlist()
    {
        Assert.All(
            BusinessInfoKeyNames.All,
            name => Assert.Equal(name, BusinessInfoKeys.Canonicalize(name)));
    }
}
