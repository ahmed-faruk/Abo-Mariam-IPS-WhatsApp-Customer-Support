using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>Admin commercial updates are validated against the persisted constraints before writing.</summary>
public sealed class CommercialUpdateRulesTests
{
    [Fact]
    public void A_valid_change_passes_every_rule()
    {
        CommercialUpdateRules.RequireVariantId(1);
        CommercialUpdateRules.RequirePrice(0m);
        CommercialUpdateRules.RequirePrice(1250.5m);
        CommercialUpdateRules.RequireQuantity(0);
        CommercialUpdateRules.RequireActor("admin-1");
    }

    [Fact]
    public void A_negative_price_is_rejected_by_the_price_constraint_rule()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialUpdateRules.RequirePrice(-0.01m));
    }

    [Fact]
    public void A_negative_quantity_is_rejected_by_the_quantity_constraint_rule()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialUpdateRules.RequireQuantity(-1));
    }

    [Fact]
    public void A_non_positive_variant_id_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialUpdateRules.RequireVariantId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialUpdateRules.RequireVariantId(-4));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_actor_is_rejected_because_the_change_must_be_audited(string? actor)
    {
        Assert.Throws<ArgumentException>(() => CommercialUpdateRules.RequireActor(actor));
    }
}
