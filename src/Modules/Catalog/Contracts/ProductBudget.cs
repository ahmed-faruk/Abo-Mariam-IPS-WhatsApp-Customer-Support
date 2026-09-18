namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>The budget the customer stated, exactly as the NLU contract reports it.</summary>
public sealed record ProductBudget
{
    public BudgetType Type { get; init; } = BudgetType.None;

    /// <summary>The soft target or the hard ceiling, depending on <see cref="Type"/>.</summary>
    public decimal? Target { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    /// <summary>“مش عايز أعدي 2500”: nothing above the ceiling may be recommended.</summary>
    public static ProductBudget Hard(decimal ceiling) => new() { Type = BudgetType.Hard, Target = ceiling };

    /// <summary>“عايز حاجة في حدود 3000”: close to the target, with a configured tolerance.</summary>
    public static ProductBudget Soft(decimal target) => new() { Type = BudgetType.Soft, Target = target };

    /// <summary>An explicit inclusive price range.</summary>
    public static ProductBudget Range(decimal min, decimal max) => new()
    {
        Type = BudgetType.Range,
        Min = min,
        Max = max,
    };
}
