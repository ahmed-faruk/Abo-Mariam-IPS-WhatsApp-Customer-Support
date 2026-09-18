namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// A search query after normalization, with the budget already resolved into inclusive price bounds
/// and the result limit already bounded. This is the exact input of the PostgreSQL search statement,
/// so the deterministic normalization is decided in one place instead of being repeated in SQL.
/// </summary>
public sealed record CatalogSearchCriteria
{
    /// <summary>The normalized lexical term, or null when the caller supplied none.</summary>
    public string? Text { get; init; }

    public string? ModelCode { get; init; }

    public string? Brand { get; init; }

    public decimal? SizeInches { get; init; }

    /// <summary>How far the stored size may differ from the requested one, in inches.</summary>
    public decimal SizeToleranceInches { get; init; }

    public string? PanelType { get; init; }

    public int? MinResolutionWidth { get; init; }

    public int? MinResolutionHeight { get; init; }

    public int? MinRefreshRate { get; init; }

    /// <summary>The normalized required port types the model must offer all of.</summary>
    public IReadOnlyList<string> RequiredPorts { get; init; } = [];

    /// <summary>The canonical accepted grades, or an empty list for every grade.</summary>
    public IReadOnlyList<string> Grades { get; init; } = [];

    public decimal? BudgetMin { get; init; }

    public decimal? BudgetMax { get; init; }

    /// <summary>The stated target, used only to order eligible models by budget closeness.</summary>
    public decimal? BudgetTarget { get; init; }

    public string? UseCase { get; init; }

    /// <summary>The bounded number of recommendations to return. It is always positive.</summary>
    public int Limit { get; init; }
}
