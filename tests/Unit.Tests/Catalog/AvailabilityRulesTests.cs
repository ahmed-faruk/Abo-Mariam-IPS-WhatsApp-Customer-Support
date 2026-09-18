using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// The availability rule of docs/PLAN.md section 7: active model, active variant, and stock.
/// </summary>
public sealed class AvailabilityRulesTests
{
    [Fact]
    public void An_active_variant_with_stock_of_an_active_model_is_available()
    {
        Assert.True(AvailabilityRules.IsAvailable(isModelActive: true, isVariantActive: true, quantity: 1));
    }

    [Fact]
    public void An_inactive_model_makes_every_variant_unavailable()
    {
        Assert.False(AvailabilityRules.IsAvailable(isModelActive: false, isVariantActive: true, quantity: 5));
    }

    [Fact]
    public void An_inactive_variant_is_unavailable_even_with_stock()
    {
        Assert.False(AvailabilityRules.IsAvailable(isModelActive: true, isVariantActive: false, quantity: 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_variant_without_stock_is_unavailable(int quantity)
    {
        Assert.False(AvailabilityRules.IsAvailable(isModelActive: true, isVariantActive: true, quantity));
    }
}
