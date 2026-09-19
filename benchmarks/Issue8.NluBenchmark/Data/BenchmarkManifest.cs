using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Reproducibility inputs for the benchmark. Thresholds live here so the harness never
/// restates a number that docs/PLAN.md section 13.3 already owns.
/// </summary>
public sealed record BenchmarkManifest
{
    public required string BenchmarkFormatVersion { get; init; }

    public required string HarnessVersion { get; init; }

    public required string SourceDocument { get; init; }

    public required ManifestDataset Dataset { get; init; }

    public required ManifestSchema Schema { get; init; }

    public required ManifestPrompt Prompt { get; init; }

    public required ManifestCandidate DefaultCandidate { get; init; }

    public required AcceptanceGateThresholds AcceptanceGates { get; init; }

    public required string[] RequiredCoverageTags { get; init; }

    public sealed record ManifestDataset
    {
        public required string Path { get; init; }

        public required string Version { get; init; }

        public required int CaseCount { get; init; }

        public required string Sha256 { get; init; }
    }

    public sealed record ManifestSchema
    {
        public required string Path { get; init; }

        public required string Version { get; init; }

        public required string Sha256 { get; init; }
    }

    public sealed record ManifestPrompt
    {
        public required string Version { get; init; }
    }

    public sealed record ManifestCandidate
    {
        public required string Model { get; init; }

        public required string BaseUrl { get; init; }

        public required int TimeoutSeconds { get; init; }

        public required int Temperature { get; init; }

        public required int ContextTokens { get; init; }

        public required string RetryPolicy { get; init; }
    }

    /// <summary>docs/PLAN.md section 13.3 demo acceptance targets, verbatim.</summary>
    public sealed record AcceptanceGateThresholds
    {
        public required decimal IntentAccuracyPercent { get; init; }

        public required decimal HardBudgetAccuracyPercent { get; init; }

        public required decimal SchemaSuccessPercent { get; init; }

        public required decimal WarmMedianSeconds { get; init; }

        public required decimal WarmP95Seconds { get; init; }
    }

    public static BenchmarkManifest Load(RepositoryPaths paths)
    {
        if (!File.Exists(paths.ManifestFile))
        {
            throw new BenchmarkDataException($"Manifest not found at {paths.ManifestFile}.");
        }

        try
        {
            return BenchmarkJson.Deserialize<BenchmarkManifest>(File.ReadAllText(paths.ManifestFile));
        }
        catch (JsonException exception)
        {
            throw new BenchmarkDataException($"Manifest {paths.ManifestFile} is not valid JSON.", exception);
        }
    }

    /// <summary>Reproducibility inputs that must be present and internally consistent.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BenchmarkFormatVersion))
        {
            errors.Add("benchmarkFormatVersion must not be empty");
        }

        if (string.IsNullOrWhiteSpace(HarnessVersion))
        {
            errors.Add("harnessVersion must not be empty");
        }

        if (string.IsNullOrWhiteSpace(SourceDocument) || !SourceDocument.Contains("v3.2", StringComparison.Ordinal))
        {
            errors.Add("sourceDocument must name the v3.2 source documents the benchmark measures against");
        }

        if (string.IsNullOrWhiteSpace(Dataset.Version) || string.IsNullOrWhiteSpace(Dataset.Sha256))
        {
            errors.Add("dataset version and sha256 must not be empty");
        }

        if (!string.Equals(Dataset.Path, RepositoryPaths.DatasetRelativePath, StringComparison.Ordinal))
        {
            errors.Add(
                $"dataset path must be '{RepositoryPaths.DatasetRelativePath}' with the exact case of the "
                + "tracked file (Linux runners are case-sensitive)");
        }

        if (string.IsNullOrWhiteSpace(Schema.Version) || string.IsNullOrWhiteSpace(Schema.Sha256))
        {
            errors.Add("schema version and sha256 must not be empty");
        }

        if (Dataset.CaseCount <= 0)
        {
            errors.Add("dataset caseCount must be positive");
        }

        if (string.IsNullOrWhiteSpace(Prompt.Version))
        {
            errors.Add("prompt version must not be empty");
        }

        if (string.IsNullOrWhiteSpace(DefaultCandidate.Model) || string.IsNullOrWhiteSpace(DefaultCandidate.BaseUrl))
        {
            errors.Add("defaultCandidate model and baseUrl must not be empty");
        }

        if (DefaultCandidate.TimeoutSeconds <= 0)
        {
            errors.Add("defaultCandidate timeoutSeconds must be positive");
        }

        if (DefaultCandidate.ContextTokens <= 0)
        {
            errors.Add("defaultCandidate contextTokens must be positive");
        }

        if (!string.Equals(DefaultCandidate.RetryPolicy, RetryPolicyOneRetryMaximum, StringComparison.Ordinal))
        {
            errors.Add($"defaultCandidate retryPolicy must be '{RetryPolicyOneRetryMaximum}'");
        }

        foreach (var error in ValidateGates())
        {
            errors.Add(error);
        }

        if (RequiredCoverageTags.Length == 0)
        {
            errors.Add("requiredCoverageTags must not be empty");
        }

        return errors;
    }

    public const string RetryPolicyOneRetryMaximum = "one-retry-maximum";

    private IEnumerable<string> ValidateGates()
    {
        if (AcceptanceGates.IntentAccuracyPercent is <= 0 or > 100)
        {
            yield return "acceptanceGates intentAccuracyPercent must be in (0, 100]";
        }

        if (AcceptanceGates.HardBudgetAccuracyPercent is <= 0 or > 100)
        {
            yield return "acceptanceGates hardBudgetAccuracyPercent must be in (0, 100]";
        }

        if (AcceptanceGates.SchemaSuccessPercent is <= 0 or > 100)
        {
            yield return "acceptanceGates schemaSuccessPercent must be in (0, 100]";
        }

        if (AcceptanceGates.WarmMedianSeconds <= 0)
        {
            yield return "acceptanceGates warmMedianSeconds must be positive";
        }

        if (AcceptanceGates.WarmP95Seconds <= 0)
        {
            yield return "acceptanceGates warmP95Seconds must be positive";
        }

        if (AcceptanceGates.WarmMedianSeconds > AcceptanceGates.WarmP95Seconds)
        {
            yield return "acceptanceGates warmMedianSeconds must not exceed warmP95Seconds";
        }
    }
}
