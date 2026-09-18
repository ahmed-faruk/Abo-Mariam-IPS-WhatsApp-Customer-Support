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
    /// target, and it widens it above the target while still ranking close to it. The widened bound
    /// never exceeds <see cref="CatalogPriceBounds.MaxSellingPrice"/>, because no stored variant can be
    /// priced above it, and it therefore never overflows for an oversized target or tolerance.
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
                SoftMaximum(RequireAmount(budget.Target, nameof(ProductBudget.Target)), softTolerance)),
            BudgetType.Range => ResolveRange(budget),
            _ => throw new ArgumentOutOfRangeException(nameof(budget), budget.Type, "The budget type is not supported."),
        };
    }

    /// <summary>
    /// The documented soft semantics, <c>target * (1 + tolerance)</c>, saturated at the largest price
    /// the selling-price column can store. The widening is computed as <c>target + target * tolerance</c>
    /// so the factor <c>1 + tolerance</c> is never formed, and the tolerance that reaches the storage
    /// bound is compared before the product is taken, so no oversized target or tolerance overflows.
    /// </summary>
    private static decimal SoftMaximum(decimal target, decimal tolerance)
    {
        if (target <= 0)
        {
            return target;
        }

        if (target > CatalogPriceBounds.MaxSellingPrice)
        {
            return CatalogPriceBounds.MaxSellingPrice;
        }

        // Below this ratio no representable tolerance can reach the storage bound, and even an extreme
        // one keeps the widening representable, so the documented value is computed exactly.
        if (target < CatalogPriceBounds.MaxSellingPrice / decimal.MaxValue)
        {
            return target + (target * tolerance);
        }

        // The target is within the storage bound, so the quotient is representable and the tolerance
        // that saturates the bound is at least zero.
        var saturatingTolerance = (CatalogPriceBounds.MaxSellingPrice / target) - 1m;

        return tolerance >= saturatingTolerance
            ? CatalogPriceBounds.MaxSellingPrice
            : target + (target * tolerance);
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
