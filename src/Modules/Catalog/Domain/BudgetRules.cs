using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>The inclusive price bounds a resolved budget applies to the search.</summary>
/// <param name="Min">The inclusive lower bound, or null when there is none.</param>
/// <param name="Max">The inclusive upper bound, or null when there is none.</param>
public sealed record BudgetScope(decimal? Min, decimal? Max);

/// <summary>
/// Budget resolution from docs/TECHNICAL.md section 11. The hard ceiling is the invariant of
/// docs/PLAN.md UC-03: no resolution path, and no tolerance, can ever raise it.
/// </summary>
public static class BudgetRules
{
    /// <summary>
    /// Resolves the stated budget into inclusive price bounds. Only a soft budget widens the stated
    /// target, and it widens it above the target while still ranking close to it.
    /// </summary>
    public static BudgetScope Resolve(ProductBudget? budget, decimal softTolerance)
    {
        if (softTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(softTolerance),
                softTolerance,
                "The soft budget tolerance must not be negative.");
        }

        if (budget is null || budget.Type == BudgetType.None)
        {
            return new BudgetScope(null, null);
        }

        return budget.Type switch
        {
            BudgetType.Hard => new BudgetScope(null, RequireAmount(budget.Target, nameof(ProductBudget.Target))),
            BudgetType.Soft => new BudgetScope(
                null,
                RequireAmount(budget.Target, nameof(ProductBudget.Target)) * (1 + softTolerance)),
            BudgetType.Range => ResolveRange(budget),
            _ => throw new ArgumentOutOfRangeException(nameof(budget), budget.Type, "The budget type is not supported."),
        };
    }

    private static BudgetScope ResolveRange(ProductBudget budget)
    {
        var min = RequireAmount(budget.Min, nameof(ProductBudget.Min));
        var max = RequireAmount(budget.Max, nameof(ProductBudget.Max));

        if (min > max)
        {
            throw new ArgumentException("The budget minimum must not be above the budget maximum.", nameof(budget));
        }

        return new BudgetScope(min, max);
    }

    private static decimal RequireAmount(decimal? amount, string name)
    {
        if (amount is null)
        {
            throw new ArgumentException($"A budget of this type requires a value for {name}.", nameof(amount));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A budget amount must not be negative.");
        }

        return amount.Value;
    }
}
