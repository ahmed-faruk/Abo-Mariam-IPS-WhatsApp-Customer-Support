namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// The structured NLU output exactly as documented in docs/TECHNICAL.md section 8.3.
/// A null value means "not provided or explicitly null"; the schema decides which fields
/// must be present. No field beyond the documented contract may appear.
/// </summary>
public sealed record NluOutput
{
    public string? Intent { get; init; }

    public string? Brand { get; init; }

    public string? ModelCode { get; init; }

    public decimal? SizeInches { get; init; }

    public string? Panel { get; init; }

    public string? Resolution { get; init; }

    public int? MinRefreshRate { get; init; }

    public string[]? RequiredPorts { get; init; }

    public string[]? Grades { get; init; }

    public string? BudgetType { get; init; }

    public decimal? BudgetTarget { get; init; }

    public decimal? BudgetMin { get; init; }

    public decimal? BudgetMax { get; init; }

    public string? UseCase { get; init; }

    public string? Reference { get; init; }
}
