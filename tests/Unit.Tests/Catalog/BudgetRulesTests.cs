using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// Budget semantics of docs/TECHNICAL.md section 11, including the hard ceiling invariant of
/// docs/PLAN.md UC-03.
/// </summary>
public sealed class BudgetRulesTests
{
    /// <summary>An explicit test input; the module defines no tolerance of its own.</summary>
    private const decimal Tolerance = 0.2m;

    [Fact]
    public void A_hard_budget_is_a_ceiling_that_no_tolerance_can_raise()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Hard(2500m), Tolerance);

        Assert.Null(scope.Min);
        Assert.Equal(2500m, scope.Max);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(5)]
    public void A_hard_ceiling_stays_exact_for_every_tolerance(double tolerance)
    {
        var scope = BudgetRules.Resolve(ProductBudget.Hard(2500m), (decimal)tolerance);

        Assert.Equal(2500m, scope.Max);
        Assert.True(scope.Max <= 2500m);
    }

    [Fact]
    public void A_soft_budget_widens_above_the_stated_target_by_the_configured_tolerance()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(3000m), Tolerance);

        Assert.Null(scope.Min);
        Assert.Equal(3600m, scope.Max);
    }

    [Fact]
    public void A_soft_budget_with_no_tolerance_is_the_stated_target()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(3000m), 0m);

        Assert.Equal(3000m, scope.Max);
    }

    [Fact]
    public void A_range_budget_is_inclusive_at_both_ends()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Range(2000m, 3000m), Tolerance);

        Assert.Equal(2000m, scope.Min);
        Assert.Equal(3000m, scope.Max);
    }

    [Fact]
    public void No_stated_budget_applies_no_price_bound()
    {
        var none = BudgetRules.Resolve(null, Tolerance);
        var explicitNone = BudgetRules.Resolve(new ProductBudget { Type = BudgetType.None }, Tolerance);

        Assert.Null(none.Min);
        Assert.Null(none.Max);
        Assert.Null(explicitNone.Min);
        Assert.Null(explicitNone.Max);
    }

    [Fact]
    public void A_hard_ceiling_of_zero_is_allowed_and_still_exact()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Hard(0m), Tolerance);

        Assert.Equal(0m, scope.Max);
    }

    [Fact]
    public void A_negative_tolerance_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetRules.Resolve(ProductBudget.Soft(3000m), -0.1m));
    }

    [Fact]
    public void A_soft_budget_of_the_largest_decimal_saturates_at_the_storage_bound()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(decimal.MaxValue), Tolerance);

        Assert.Equal(CatalogPriceBounds.MaxSellingPrice, scope.Max);
    }

    [Fact]
    public void A_soft_budget_just_below_the_largest_decimal_still_resolves_without_overflow()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(decimal.MaxValue - 1m), Tolerance);

        Assert.Equal(CatalogPriceBounds.MaxSellingPrice, scope.Max);
    }

    [Fact]
    public void A_soft_budget_can_never_resolve_above_the_largest_storable_price()
    {
        // The widened bound would be larger than the column can store, so it saturates at the bound
        // instead of overflowing or promising a price no stored variant could have.
        var scope = BudgetRules.Resolve(ProductBudget.Soft(CatalogPriceBounds.MaxSellingPrice - 1m), 0.9m);

        Assert.Equal(CatalogPriceBounds.MaxSellingPrice, scope.Max);
    }

    [Fact]
    public void A_soft_budget_below_the_storage_bound_keeps_the_documented_widening()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(3_000_000_000m), 0.2m);

        Assert.Equal(3_600_000_000m, scope.Max);
    }

    [Fact]
    public void An_extreme_tolerance_saturates_instead_of_overflowing()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Soft(3000m), decimal.MaxValue);

        Assert.Equal(CatalogPriceBounds.MaxSellingPrice, scope.Max);
    }

    [Fact]
    public void A_tiny_target_with_an_extreme_tolerance_is_still_computed_without_overflow()
    {
        // The widening stays far below the storage bound for such a target, so nothing saturates and
        // nothing overflows while the extreme tolerance is applied.
        var scope = BudgetRules.Resolve(ProductBudget.Soft(0.0000000000000000001m), decimal.MaxValue);

        Assert.True(scope.Max > 0m);
        Assert.True(scope.Max < CatalogPriceBounds.MaxSellingPrice);
    }

    [Fact]
    public void A_hard_ceiling_of_the_largest_decimal_is_still_exactly_the_stated_ceiling()
    {
        var scope = BudgetRules.Resolve(ProductBudget.Hard(decimal.MaxValue), Tolerance);

        Assert.Equal(decimal.MaxValue, scope.Max);
    }

    [Fact]
    public void A_negative_budget_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetRules.Resolve(ProductBudget.Hard(-1m), Tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetRules.Resolve(ProductBudget.Soft(-1m), Tolerance));
    }

    [Fact]
    public void A_budget_type_without_its_required_amount_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => BudgetRules.Resolve(new ProductBudget { Type = BudgetType.Hard }, Tolerance));
        Assert.Throws<ArgumentException>(
            () => BudgetRules.Resolve(new ProductBudget { Type = BudgetType.Soft }, Tolerance));
        Assert.Throws<ArgumentException>(
            () => BudgetRules.Resolve(new ProductBudget { Type = BudgetType.Range, Min = 1000m }, Tolerance));
    }

    [Fact]
    public void A_range_whose_minimum_is_above_its_maximum_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => BudgetRules.Resolve(ProductBudget.Range(3000m, 2000m), Tolerance));
    }
}
