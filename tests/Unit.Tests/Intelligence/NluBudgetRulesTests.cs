using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The budget rules are the domain decision behind the validator's budget checks: each budget type
/// owns its fields, and no budget may be zero or negative. They never resolve a tolerance; that
/// belongs to Catalogue search.
/// </summary>
public sealed class NluBudgetRulesTests
{
    [Fact]
    public void A_consistent_budget_shape_has_no_problems()
    {
        Assert.Empty(NluBudgetRules.Validate(NluBudgetType.None, null, null, null));
        Assert.Empty(NluBudgetRules.Validate(NluBudgetType.Soft, 3000m, null, null));
        Assert.Empty(NluBudgetRules.Validate(NluBudgetType.Hard, 2500m, null, null));
        Assert.Empty(NluBudgetRules.Validate(NluBudgetType.Range, null, 2000m, 4000m));
    }

    [Fact]
    public void No_budget_may_not_carry_a_value()
    {
        var problems = NluBudgetRules.Validate(NluBudgetType.None, 3000m, null, null);

        Assert.Contains(problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void A_soft_or_hard_budget_may_not_carry_range_bounds()
    {
        Assert.Contains(
            NluBudgetRules.Validate(NluBudgetType.Soft, 3000m, 2000m, null),
            problem => problem.StartsWith("$.budgetMin", StringComparison.Ordinal));
        Assert.Contains(
            NluBudgetRules.Validate(NluBudgetType.Hard, 3000m, null, 4000m),
            problem => problem.StartsWith("$.budgetMax", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_may_not_end_below_its_start()
    {
        var problems = NluBudgetRules.Validate(NluBudgetType.Range, null, 4000m, 2000m);

        Assert.Contains(problems, problem => problem.StartsWith("$.budgetMax", StringComparison.Ordinal));
    }

    [Fact]
    public void A_budget_value_must_be_positive()
    {
        Assert.Contains(
            NluBudgetRules.Validate(NluBudgetType.Soft, 0m, null, null),
            problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
        Assert.Contains(
            NluBudgetRules.Validate(NluBudgetType.Range, null, -1m, 4000m),
            problem => problem.StartsWith("$.budgetMin", StringComparison.Ordinal));
    }

    [Fact]
    public void An_undefined_budget_type_value_is_rejected()
    {
        var problems = NluBudgetRules.Validate((NluBudgetType)99, null, null, null);

        Assert.Contains(problems, problem => problem.StartsWith("$.budgetType", StringComparison.Ordinal));
    }
}
