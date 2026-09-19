namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// One versioned benchmark case. <see cref="Expected"/> is authored from PLAN/TECHNICAL and
/// must never be produced by the model under test. <see cref="Gating"/> is false only for
/// observational cases where the source of truth does not define a structured answer;
/// those cases are reported but never counted towards a gate.
/// </summary>
public sealed record BenchmarkCase
{
    public required string Id { get; init; }

    public required string Input { get; init; }

    public required NluOutput Expected { get; init; }

    public string[] Tags { get; init; } = [];

    public bool HardBudgetCase { get; init; }

    public bool Gating { get; init; } = true;

    public string? Notes { get; init; }
}
