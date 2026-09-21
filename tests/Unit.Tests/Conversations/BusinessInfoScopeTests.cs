using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// A business-info question resolves to an approved Storefront key through an explicit deterministic
/// allowlist over the original customer text. An unmatched question is a clarification, never a guess
/// at which stored answer the customer meant.
/// </summary>
public sealed class BusinessInfoScopeTests
{
    [Theory]
    [InlineData("مواعيدكم إيه؟", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("ايه مواعيد الشغل", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("what are your working hours", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("فين المكان", BusinessInfoKeyNames.Address)]
    [InlineData("العنوان ايه", BusinessInfoKeyNames.Address)]
    [InlineData("where are you located", BusinessInfoKeyNames.Address)]
    [InlineData("في توصيل", BusinessInfoKeyNames.Delivery)]
    [InlineData("do you deliver", BusinessInfoKeyNames.Delivery)]
    [InlineData("بتقبلوا فيزا", BusinessInfoKeyNames.PaymentMethods)]
    [InlineData("طرق الدفع", BusinessInfoKeyNames.PaymentMethods)]
    [InlineData("الضمان كام", BusinessInfoKeyNames.Warranty)]
    [InlineData("رقم التليفون", BusinessInfoKeyNames.ContactPhone)]
    [InlineData("الاسترجاع", BusinessInfoKeyNames.ReturnExchangePolicy)]
    public void An_allowlisted_question_resolves_its_canonical_storefront_key(string text, string expectedKey)
    {
        var resolved = BusinessInfoScope.ResolveKey(text);

        Assert.Equal(expectedKey, resolved);
        Assert.Contains(resolved, BusinessInfoKeyNames.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("حاجة تانية خالص")]
    [InlineData("tell me about the monitors")]
    public void A_question_outside_the_allowlist_resolves_to_nothing(string? text)
    {
        Assert.Null(BusinessInfoScope.ResolveKey(text));
    }
}
