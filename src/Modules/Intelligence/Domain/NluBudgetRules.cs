using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

/// <summary>
/// The budget consistency rules of the structured contract: which budget fields may be filled for
/// each <see cref="NluBudgetType"/>, and which values are meaningful at all. They validate what the
/// model said about the customer's budget; they never resolve a budget into a price range, because
/// the soft tolerance and the hard ceiling belong to Catalogue search (docs/TECHNICAL.md section 11).
/// </summary>
public static class NluBudgetRules
{
    /// <summary>
    /// Returns the problems of one budget shape. An empty result means the fields agree with the type.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        NluBudgetType budgetType,
        decimal? target,
        decimal? min,
        decimal? max)
    {
        var problems = new List<string>();

        switch (budgetType)
        {
            case NluBudgetType.None:
                RequireNull(problems, "budgetTarget", target);
                RequireNull(problems, "budgetMin", min);
                RequireNull(problems, "budgetMax", max);
                break;

            case NluBudgetType.Soft:
            case NluBudgetType.Hard:
                RequirePositiveTarget(problems, budgetType, target);
                RequireNull(problems, "budgetMin", min);
                RequireNull(problems, "budgetMax", max);
                break;

            case NluBudgetType.Range:
                RequireNull(problems, "budgetTarget", target);
                RequirePositive(problems, "budgetMin", min, NluBudgetType.Range);
                RequirePositive(problems, "budgetMax", max, NluBudgetType.Range);

                if (min is > 0 && max is > 0 && min > max)
                {
                    problems.Add(
                        "$.budgetMax: a range must not end below its $.budgetMin");
                }

                break;

            default:
                problems.Add("$.budgetType: is not one of None, Soft, Hard or Range");
                break;
        }

        return problems;
    }

    private static void RequirePositiveTarget(
        List<string> problems,
        NluBudgetType budgetType,
        decimal? target)
    {
        if (target is not > 0)
        {
            problems.Add(
                "$.budgetTarget: is required and must be greater than zero for a "
                + $"{budgetType} budget");
        }
    }

    private static void RequirePositive(
        List<string> problems,
        string field,
        decimal? value,
        NluBudgetType budgetType)
    {
        if (value is not > 0)
        {
            problems.Add(
                $"$.{field}: is required and must be greater than zero for a {budgetType} budget");
        }
    }

    private static void RequireNull(List<string> problems, string field, decimal? value)
    {
        if (value is not null)
        {
            problems.Add($"$.{field}: must be null for this budget type");
        }
    }
}
