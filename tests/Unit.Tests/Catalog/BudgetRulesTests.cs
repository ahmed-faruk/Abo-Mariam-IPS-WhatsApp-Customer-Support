using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// Budget semantics of docs/TECHNICAL.md section 11, including the hard ceiling invariant of
/// docs/PLAN.md UC-03.
/// </summary>
public sealed class BudgetRulesTests
{
    private const decimal Tolerance = 0.15m;

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
        Assert.Equal(3450m, scope.Max);
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
