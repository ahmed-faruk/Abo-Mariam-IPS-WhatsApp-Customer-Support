using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Storefront;

/// <summary>
/// The allowlist is the single policy that decides which business-info keys exist, so reads and writes
/// cannot drift and no caller can invent one.
/// </summary>
public sealed class BusinessInfoKeysTests
{
    [Fact]
    public void The_allowlist_holds_exactly_the_seven_approved_concepts()
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
            BusinessInfoKeys.All);

        Assert.Equal(
            [
                BusinessInfoKeys.WorkingHours,
                BusinessInfoKeys.Address,
                BusinessInfoKeys.Delivery,
                BusinessInfoKeys.PaymentMethods,
                BusinessInfoKeys.Warranty,
                BusinessInfoKeys.ContactPhone,
                BusinessInfoKeys.ReturnExchangePolicy,
            ],
            BusinessInfoKeys.All);
    }

    [Theory]
    [InlineData(BusinessInfoKeys.WorkingHours)]
    [InlineData(BusinessInfoKeys.Address)]
    [InlineData(BusinessInfoKeys.Delivery)]
    [InlineData(BusinessInfoKeys.PaymentMethods)]
    [InlineData(BusinessInfoKeys.Warranty)]
    [InlineData(BusinessInfoKeys.ContactPhone)]
    [InlineData(BusinessInfoKeys.ReturnExchangePolicy)]
    public void Every_approved_key_canonicalizes_to_itself(string key) =>
        Assert.Equal(key, BusinessInfoKeys.Canonicalize(key));

    [Theory]
    [InlineData(" workinghours ", BusinessInfoKeys.WorkingHours)]
    [InlineData("WORKINGHOURS", BusinessInfoKeys.WorkingHours)]
    [InlineData("\tpaymentmethods\n", BusinessInfoKeys.PaymentMethods)]
    [InlineData("returnExchangEPolicy", BusinessInfoKeys.ReturnExchangePolicy)]
    public void An_approved_key_is_recognized_by_case_and_outer_whitespace(string requested, string expected) =>
        Assert.Equal(expected, BusinessInfoKeys.Canonicalize(requested));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("opening-times")]
    [InlineData("working_hours")]
    [InlineData("working hours")]
    [InlineData("WorkingHour")]
    [InlineData("WorkingHours2")]
    [InlineData("FAQ")]
    [InlineData("anything")]
    public void No_key_outside_the_allowlist_is_recognized_or_required(string? key)
    {
        Assert.Null(BusinessInfoKeys.Canonicalize(key));
        Assert.Throws<ArgumentException>(() => BusinessInfoKeys.RequireAllowed(key));
    }

    [Fact]
    public void An_approved_key_is_required_as_its_canonical_form()
    {
        Assert.Equal(BusinessInfoKeys.Address, BusinessInfoKeys.RequireAllowed(" ADDRESS "));
    }
}
