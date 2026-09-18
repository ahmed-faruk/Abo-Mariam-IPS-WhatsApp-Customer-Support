using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// The deterministic normalization shared by the search query and the stored values it compares
/// against.
/// </summary>
public sealed class CatalogTextTests
{
    [Theory]
    [InlineData("P2419H", "p2419h")]
    [InlineData("  P2419H ", "p2419h")]
    [InlineData("Dell UltraSharp", "dell ultrasharp")]
    [InlineData("Dell\tUltraSharp", "dell ultrasharp")]
    [InlineData("Dell   UltraSharp  ", "dell ultrasharp")]
    [InlineData("Dell\n UltraSharp", "dell ultrasharp")]
    [InlineData("شاشة ديل 24", "شاشة ديل 24")]
    [InlineData("", "")]
    public void Normalization_is_trimmed_collapsed_and_invariant_lowercase(string value, string expected)
    {
        Assert.Equal(expected, CatalogText.Normalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void A_blank_value_is_no_filter_at_all(string? value)
    {
        Assert.Null(CatalogText.NormalizeOrNull(value));
    }

    [Fact]
    public void Normalizing_missing_text_is_rejected_rather_than_guessed()
    {
        Assert.Throws<ArgumentNullException>(() => CatalogText.Normalize(null!));
    }
}
