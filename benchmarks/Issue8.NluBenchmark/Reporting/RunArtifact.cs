using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

public static class RunModes
{
    public const string Live = "live";
    public const string DryRun = "dry-run";
}

public sealed record RunSettings
{
    public required int Temperature { get; init; }

    public required int ContextTokens { get; init; }

    public required int TimeoutSeconds { get; init; }

    public required string RetryPolicy { get; init; }
}

public sealed record RunCaseRecord
{
    public required string CaseId { get; init; }

    public required bool SchemaValid { get; init; }

    public required bool Retried { get; init; }

    public required long FinalAttemptMilliseconds { get; init; }

    public required long TotalMilliseconds { get; init; }

    public NluOutput? Output { get; init; }

    public string? FailureReason { get; init; }

    public required RunAttemptRecord[] Attempts { get; init; }
}

public sealed record RunAttemptRecord
{
    public required bool Correction { get; init; }

    public required string RawContent { get; init; }

    public required long WallClockMilliseconds { get; init; }

    public required bool SchemaValid { get; init; }

    public string[] SchemaErrors { get; init; } = [];

    public string? TransportFailure { get; init; }

    public NluTransportTiming? Timing { get; init; }
}

/// <summary>
/// One full pass over the dataset. Raw attempts are kept so a mismatch can be inspected
/// without re-running the model; the file stays under the gitignored results directory.
/// </summary>
public sealed record RunArtifact
{
    public required string RunId { get; init; }

    public required string Mode { get; init; }

    public required string Model { get; init; }

    public required string BaseUrl { get; init; }

    public required string HarnessVersion { get; init; }

    public required string DatasetVersion { get; init; }

    public required string DatasetSha256 { get; init; }

    public required string SchemaVersion { get; init; }

    public required string SchemaSha256 { get; init; }

    public required string PromptVersion { get; init; }

    public required string StartedAtUtc { get; init; }

    public required string CompletedAtUtc { get; init; }

    public required RunSettings Settings { get; init; }

    public EnvironmentMetadata? Environment { get; init; }

    public required RunCaseRecord[] Cases { get; init; }

    public static RunArtifact FromExecution(
        RunContext context,
        string datasetVersion,
        string datasetSha256,
        string schemaVersion,
        string schemaSha256,
        string promptVersion,
        string startedAtUtc,
        string completedAtUtc,
        IReadOnlyList<NluCaseExecution> executions) => new()
        {
            RunId = context.RunId,
            Mode = context.Mode,
            Model = context.Model,
            BaseUrl = context.BaseUrl,
            HarnessVersion = NluContract.HarnessVersion,
            DatasetVersion = datasetVersion,
            DatasetSha256 = datasetSha256,
            SchemaVersion = schemaVersion,
            SchemaSha256 = schemaSha256,
            PromptVersion = promptVersion,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Settings = context.Settings,
            Environment = context.Environment,
            Cases =
            [
                .. executions.Select(execution => new RunCaseRecord
                {
                    CaseId = execution.CaseId,
                    SchemaValid = execution.SchemaValid,
                    Retried = execution.Retried,
                    FinalAttemptMilliseconds = execution.FinalAttemptMilliseconds,
                    TotalMilliseconds = execution.TotalMilliseconds,
                    Output = execution.Output,
                    FailureReason = execution.FailureReason,
                    Attempts =
                    [
                        .. execution.Attempts.Select(attempt => new RunAttemptRecord
                        {
                            Correction = attempt.Correction,
                            RawContent = attempt.RawContent,
                            WallClockMilliseconds = attempt.WallClockMilliseconds,
                            SchemaValid = attempt.SchemaValid,
                            SchemaErrors = attempt.SchemaErrors,
                            TransportFailure = attempt.TransportFailure,
                            Timing = attempt.Timing,
                        }),
                    ],
                }),
            ],
        };
}

public sealed class RunArtifactStore
{
    private readonly string _directory;

    public RunArtifactStore(string directory)
    {
        _directory = directory;
    }

    public string PathFor(string runId) => Path.Combine(_directory, $"{runId}.json");

    public void Save(RunArtifact artifact)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(artifact.RunId), JsonSerializer.Serialize(artifact, BenchmarkJson.Options));
    }

    public RunArtifact Load(string runId)
    {
        var path = PathFor(runId);

        if (!File.Exists(path))
        {
            throw new BenchmarkDataException($"Run artifact {path} was not found. Run the benchmark first.");
        }

        try
        {
            return BenchmarkJson.Deserialize<RunArtifact>(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new BenchmarkDataException($"Run artifact {path} is not valid JSON.", exception);
        }
    }
}
