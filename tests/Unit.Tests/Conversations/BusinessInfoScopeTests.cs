using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// A business-info question resolves to the approved Storefront keys it names through an explicit
/// deterministic allowlist over the original customer text. An unmatched question is a clarification,
/// and a question that names two different concepts is ambiguous rather than silently answered with the
/// first one, so the customer is asked which answer they meant.
/// </summary>
public sealed class BusinessInfoScopeTests
{
    [Theory]
    [InlineData("مواعيدكم إيه؟", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("ايه مواعيد الشغل", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("what are your working hours", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("فاتحين امتى؟", BusinessInfoKeyNames.WorkingHours)]
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
        var resolved = Assert.Single(BusinessInfoScope.ResolveKeys(text));

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
        Assert.Empty(BusinessInfoScope.ResolveKeys(text));
    }

    [Fact]
    public void A_question_naming_two_concepts_resolves_both_instead_of_picking_the_first()
    {
        Assert.Equal(
            [BusinessInfoKeyNames.WorkingHours, BusinessInfoKeyNames.Address],
            BusinessInfoScope.ResolveKeys("مواعيدكم إيه والعنوان فين؟"));
    }

    [Fact]
    public void Repeated_aliases_of_one_concept_stay_one_key()
    {
        Assert.Equal(
            [BusinessInfoKeyNames.WorkingHours],
            BusinessInfoScope.ResolveKeys("مواعيد الشغل وكمان معاد الفتح working hours"));
    }

    [Fact]
    public void The_order_of_the_aliases_in_the_question_does_not_change_the_keys()
    {
        Assert.Equal(
            BusinessInfoScope.ResolveKeys("مواعيدكم إيه والعنوان فين؟"),
            BusinessInfoScope.ResolveKeys("العنوان فين ومواعيدكم إيه؟"));
    }

    [Theory]
    [InlineData("هل توجد إمكانية الدفع بالفيزا؟", BusinessInfoKeyNames.PaymentMethods)]
    [InlineData("إمكانية الدفع", BusinessInfoKeyNames.PaymentMethods)]
    [InlineData("فين المكان؟", BusinessInfoKeyNames.Address)]
    [InlineData("المكان فين؟", BusinessInfoKeyNames.Address)]
    public void An_arabic_alias_never_matches_inside_an_unrelated_word(string text, string expectedKey)
    {
        // "مكان" is an address alias, and "إمكانية" merely contains those letters. The address concept
        // is therefore not named by a payment question, and the question stays unambiguous.
        Assert.Equal([expectedKey], BusinessInfoScope.ResolveKeys(text));
    }

    [Fact]
    public void A_bare_number_word_resolves_to_nothing()
    {
        // "ألفين" (two thousand) is a number, not a place: the leading "ال" belongs to the number word
        // itself, so it may not be read as an article attached to the address alias "فين".
        Assert.Empty(BusinessInfoScope.ResolveKeys("ألفين"));
    }

    [Theory]
    [InlineData("السعر ألفين جنيه")]
    [InlineData("في حدود ألفين")]
    public void A_number_word_never_names_the_address_concept(string text)
    {
        Assert.DoesNotContain(BusinessInfoKeyNames.Address, BusinessInfoScope.ResolveKeys(text));
    }
}
