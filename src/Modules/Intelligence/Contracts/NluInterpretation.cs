namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// One structured interpretation of a customer message: exactly the fifteen documented fields of
/// docs/TECHNICAL.md section 8.3. The type carries no commercial fact of its own. Price, quantity,
/// availability, grade as product data, warranty, exact specifications and Storefront business
/// values stay owned by PostgreSQL, and the model only extracts what the customer asked for.
/// </summary>
public sealed record NluInterpretation
{
    /// <summary>The routed intent. It is always one of the <see cref="NluIntent"/> members.</summary>
    public required NluIntent Intent { get; init; }

    /// <summary>The manufacturer the customer named, or null when none was stated.</summary>
    public string? Brand { get; init; }

    /// <summary>The exact model code the customer named, or null when none was stated.</summary>
    public string? ModelCode { get; init; }

    /// <summary>The requested monitor size in inches, or null when none was stated.</summary>
    public decimal? SizeInches { get; init; }

    /// <summary>The requested panel technology, for example IPS, or null when none was stated.</summary>
    public string? Panel { get; init; }

    /// <summary>The requested resolution, or null when none was stated.</summary>
    public string? Resolution { get; init; }

    /// <summary>The requested minimum refresh rate in Hz, or null when none was stated.</summary>
    public int? MinRefreshRate { get; init; }

    /// <summary>The ports the customer requires. Never null; empty means no port was requested.</summary>
    public required IReadOnlyList<string> RequiredPorts { get; init; }

    /// <summary>
    /// The product-condition grades the customer asked for as a filter. Never null; empty means no
    /// grade was requested. This is a customer filter, not authoritative grade data.
    /// </summary>
    public required IReadOnlyList<string> Grades { get; init; }

    /// <summary>The stated budget shape.</summary>
    public required NluBudgetType BudgetType { get; init; }

    /// <summary>The soft target or the hard ceiling, and null for <see cref="NluBudgetType.None"/> and range.</summary>
    public decimal? BudgetTarget { get; init; }

    /// <summary>The lower bound of a range, and null otherwise.</summary>
    public decimal? BudgetMin { get; init; }

    /// <summary>The upper bound of a range, and null otherwise.</summary>
    public decimal? BudgetMax { get; init; }

    /// <summary>The curated use case the customer stated, or null when none was stated.</summary>
    public string? UseCase { get; init; }

    /// <summary>The follow-up reference the customer used, or null when none was stated.</summary>
    public string? Reference { get; init; }
}
