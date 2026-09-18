using System.Globalization;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

public sealed record RunContext
{
    public required string RunId { get; init; }

    public required string Mode { get; init; }

    public required string Model { get; init; }

    public required string BaseUrl { get; init; }

    public required RunSettings Settings { get; init; }

    public EnvironmentMetadata? Environment { get; init; }
}

public sealed record RunProgress(int Index, int Total, string CaseId, bool SchemaValid, long Milliseconds, bool Retried);

/// <summary>
/// Executes the dataset once, in file order, and produces the run artifact. Warm-up
/// requests are a separate command and never reach this runner, so cold-load latency
/// cannot leak into the reported percentiles.
/// </summary>
public sealed class BenchmarkRunner
{
    private readonly NluAnalyzer _analyzer;

    public BenchmarkRunner(NluAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public async Task<RunArtifact> RunAsync(
        BenchmarkDataset dataset,
        BenchmarkManifest manifest,
        RunContext context,
        Action<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executions = new List<NluCaseExecution>(dataset.Cases.Count);
        var index = 0;

        foreach (var testCase in dataset.Cases)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();

            var execution = await _analyzer.AnalyzeAsync(testCase, cancellationToken).ConfigureAwait(false);
            executions.Add(execution);

            progress?.Invoke(new RunProgress(
                index,
                dataset.Cases.Count,
                execution.CaseId,
                execution.SchemaValid,
                execution.FinalAttemptMilliseconds,
                execution.Retried));
        }

        var completedAt = DateTimeOffset.UtcNow;

        return RunArtifact.FromExecution(
            context,
            dataset.Version,
            dataset.Sha256,
            manifest.Schema.Version,
            manifest.Schema.Sha256,
            manifest.Prompt.Version,
            startedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            completedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            executions);
    }

    /// <summary>
    /// A run whose every case failed on transport is an infrastructure failure, not a model
    /// result, so the caller can exit non-zero without pretending the model was measured.
    /// </summary>
    public static bool IsInfrastructureFailure(RunArtifact artifact) =>
        artifact.Cases.Length > 0
        && artifact.Cases.All(testCase => testCase.Attempts.All(attempt => attempt.TransportFailure is not null));
}
