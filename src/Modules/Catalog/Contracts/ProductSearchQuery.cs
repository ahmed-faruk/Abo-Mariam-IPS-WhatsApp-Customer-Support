namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// One structured product search, as extracted from a customer message by the NLU contract of
/// docs/TECHNICAL.md section 8.3. Every filter is optional, and the module normalizes whatever it
/// receives, so callers never need to know the persisted spelling.
/// </summary>
/// <remarks>
/// The structured filters are hard filters: a returned model satisfies all of them. <see cref="Text"/>
/// and <see cref="UseCase"/> are softer, and only order the models that already passed the hard
/// filters, exactly as docs/PLAN.md ADR-M07 defines the search order.
/// </remarks>
public sealed record ProductSearchQuery
{
    /// <summary>Free text to match against the product name, brand and model code.</summary>
    public string? Text { get; init; }

    /// <summary>The exact monitor model code, for example <c>P2419H</c>.</summary>
    public string? ModelCode { get; init; }

    public string? Brand { get; init; }

    /// <summary>Nominal size in inches, for example 24.</summary>
    public decimal? SizeInches { get; init; }

    /// <summary>Panel type, for example <c>IPS</c>.</summary>
    public string? PanelType { get; init; }

    /// <summary>The smallest acceptable horizontal resolution.</summary>
    public int? MinResolutionWidth { get; init; }

    /// <summary>The smallest acceptable vertical resolution.</summary>
    public int? MinResolutionHeight { get; init; }

    public int? MinRefreshRate { get; init; }

    /// <summary>Port types the model must offer all of.</summary>
    public IReadOnlyList<string> RequiredPorts { get; init; } = [];

    /// <summary>Accepted grades. An empty list accepts every grade.</summary>
    public IReadOnlyList<string> Grades { get; init; } = [];

    public ProductBudget? Budget { get; init; }

    /// <summary>A curated use-case tag such as <c>Programming</c>, <c>Office</c> or <c>Gaming</c>.</summary>
    public string? UseCase { get; init; }

    /// <summary>
    /// The maximum number of recommendations the caller wants. It is bounded by the configured
    /// maximum, so a caller can never ask for an unbounded result set.
    /// </summary>
    public int? Limit { get; init; }
}
