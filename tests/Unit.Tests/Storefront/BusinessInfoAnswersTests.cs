using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Storefront;

/// <summary>
/// The value rules of one business-info row: the Arabic answer is required, the English answer is
/// optional, and only outer whitespace is removed.
/// </summary>
public sealed class BusinessInfoAnswersTests
{
    [Fact]
    public void The_arabic_answer_keeps_its_content_and_loses_only_outer_whitespace()
    {
        Assert.Equal("من 10 ص إلى 8 م", BusinessInfoAnswers.RequireAnswerAr("  من 10 ص إلى 8 م "));
        Assert.Equal("Two   spaces", BusinessInfoAnswers.RequireAnswerAr("Two   spaces"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_missing_or_blank_arabic_answer_is_rejected(string? answerAr) =>
        Assert.Throws<ArgumentException>(() => BusinessInfoAnswers.RequireAnswerAr(answerAr));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void A_missing_or_blank_english_answer_normalizes_to_null(string? answerEn) =>
        Assert.Null(BusinessInfoAnswers.NormalizeAnswerEn(answerEn));

    [Fact]
    public void An_english_answer_keeps_its_content_and_loses_only_outer_whitespace()
    {
        Assert.Equal(
            "Payment: cash or card.",
            BusinessInfoAnswers.NormalizeAnswerEn("  Payment: cash or card.  "));
        Assert.Equal("Two   spaces", BusinessInfoAnswers.NormalizeAnswerEn("Two   spaces"));
    }
}
