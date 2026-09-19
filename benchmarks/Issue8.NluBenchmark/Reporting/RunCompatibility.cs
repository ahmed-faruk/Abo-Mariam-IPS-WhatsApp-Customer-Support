namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Evidence-integrity checks that run before a measured artifact is evaluated and before two
/// artifacts are combined into the final report. Every artifact must describe the current
/// benchmark source, contain the full dataset exactly once, and — when runs are combined —
/// agree on the request settings and on the stable machine/runtime identity, so an old or
/// partial run can never be relabelled with current manifest metadata.
/// </summary>
public static class RunCompatibility
{
    public static void Validate(
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        IReadOnlyList<RunArtifact> runs)
    {
        if (runs.Count == 0)
        {
            throw new BenchmarkDataException("At least one run artifact is required.");
        }

        foreach (var run in runs)
        {
            ValidateCurrentSource(manifest, dataset, run);
            ValidateNoTransportFailures(run);
            ValidateCompleteCoverage(run, dataset);
        }

        if (runs.Count > 1)
        {
            ValidateConsistentSettings(runs);
            ValidateConsistentEnvironment(runs);
        }
    }

    private static void ValidateCurrentSource(
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        RunArtifact run)
    {
        var problems = new List<string>();

        CompareExact(problems, "dataset version", run.DatasetVersion, dataset.Version);
        CompareHash(problems, "dataset sha256", run.DatasetSha256, dataset.Sha256);
        CompareExact(problems, "schema version", run.SchemaVersion, manifest.Schema.Version);
        CompareHash(problems, "schema sha256", run.SchemaSha256, manifest.Schema.Sha256);
        CompareExact(problems, "prompt version", run.PromptVersion, manifest.Prompt.Version);
        CompareExact(problems, "harness version", run.HarnessVersion, NluContract.HarnessVersion);

        if (problems.Count > 0)
        {
            throw new BenchmarkDataException(
                $"Run '{run.RunId}' was produced by a different benchmark source than the current one, "
                + $"so it is not evidence for this dataset: {string.Join("; ", problems)}.");
        }
    }

    private static void ValidateNoTransportFailures(RunArtifact run)
    {
        if (!BenchmarkRunner.IsInfrastructureFailure(run))
        {
            return;
        }

        var failedCases = run.Cases
            .Where(testCase => testCase.Attempts.Any(attempt => attempt.TransportFailure is not null))
            .Select(testCase => testCase.CaseId)
            .ToArray();

        throw new BenchmarkDataException(
            $"Run '{run.RunId}' recorded no model reply for {failedCases.Length} case(s) "
            + $"({string.Join(", ", failedCases)}). A transport failure or partial outage is "
            + "infrastructure-failure evidence, not a model quality result, so the run is never "
            + "evaluated and never combined into a report.");
    }

    private static void ValidateCompleteCoverage(RunArtifact run, BenchmarkDataset dataset)
    {
        var expected = dataset.Cases.Select(testCase => testCase.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        var unexpected = new List<string>();

        foreach (var record in run.Cases)
        {
            if (!expected.Contains(record.CaseId))
            {
                unexpected.Add(record.CaseId);
            }
            else if (!seen.Add(record.CaseId))
            {
                duplicates.Add(record.CaseId);
            }
        }

        var missing = expected
            .Where(id => !seen.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0 && duplicates.Count == 0 && unexpected.Count == 0)
        {
            return;
        }

        var problems = new List<string>();

        if (missing.Length > 0)
        {
            problems.Add($"missing {missing.Length}: {string.Join(", ", missing)}");
        }

        if (duplicates.Count > 0)
        {
            problems.Add($"duplicate {duplicates.Count}: {string.Join(", ", Distinct(duplicates))}");
        }

        if (unexpected.Count > 0)
        {
            problems.Add($"not in the dataset {unexpected.Count}: {string.Join(", ", Distinct(unexpected))}");
        }

        throw new BenchmarkDataException(
            $"Run '{run.RunId}' does not contain all {dataset.Cases.Count} dataset case ids exactly once "
            + $"({string.Join("; ", problems)}).");
    }

    private static void ValidateConsistentSettings(IReadOnlyList<RunArtifact> runs)
    {
        var baseline = runs[0];
        var problems = new List<string>();

        foreach (var run in runs.Skip(1))
        {
            CompareExact(problems, "model", run.Model, baseline.Model);
            CompareExact(problems, "base url", run.BaseUrl, baseline.BaseUrl);
            CompareExact(problems, "temperature", run.Settings.Temperature, baseline.Settings.Temperature);
            CompareExact(problems, "context tokens", run.Settings.ContextTokens, baseline.Settings.ContextTokens);
            CompareExact(problems, "timeout seconds", run.Settings.TimeoutSeconds, baseline.Settings.TimeoutSeconds);
            CompareExact(problems, "retry policy", run.Settings.RetryPolicy, baseline.Settings.RetryPolicy);
        }

        if (problems.Count > 0)
        {
            throw new BenchmarkDataException(
                $"Runs to combine were measured with different request settings: {string.Join("; ", problems)}. "
                + "Regenerate both runs from the same configuration.");
        }
    }

    /// <summary>
    /// Compares the stable machine/runtime identity only. Run timestamps and
    /// <see cref="EnvironmentMetadata.CollectedAtUtc"/> are deliberately ignored so two passes
    /// on the same machine combine cleanly.
    /// </summary>
    private static void ValidateConsistentEnvironment(IReadOnlyList<RunArtifact> runs)
    {
        var problems = new List<string>();

        foreach (var run in runs.Skip(1))
        {
            problems.AddRange(CompareEnvironment(runs[0], run));
        }

        if (problems.Count > 0)
        {
            throw new BenchmarkDataException(
                $"Runs to combine were not measured on one machine/runtime baseline: {string.Join("; ", problems)}.");
        }
    }

    private static IEnumerable<string> CompareEnvironment(RunArtifact baseline, RunArtifact run)
    {
        if (baseline.Environment is null || run.Environment is null)
        {
            yield return $"{baseline.RunId} and {run.RunId} must both record environment metadata";
            yield break;
        }

        var expected = baseline.Environment;
        var actual = run.Environment;

        foreach (var (field, expectedValue, actualValue) in new (string Field, string? Expected, string? Actual)[]
        {
            ("osVersion", expected.OsVersion, actual.OsVersion),
            ("architecture", expected.Architecture, actual.Architecture),
            ("cpu", expected.Cpu, actual.Cpu),
            ("ollamaVersion", expected.OllamaVersion, actual.OllamaVersion),
        })
        {
            if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
            {
                yield return $"{field}: {baseline.RunId}='{expectedValue}' vs {run.RunId}='{actualValue}'";
            }
        }

        if (expected.MemoryBytes != actual.MemoryBytes)
        {
            yield return $"memoryBytes: {baseline.RunId}='{expected.MemoryBytes}' vs {run.RunId}='{actual.MemoryBytes}'";
        }
    }

    private static string[] Distinct(IEnumerable<string> values) =>
        [.. values.Distinct(StringComparer.Ordinal)];

    private static void CompareExact<T>(List<string> problems, string field, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            problems.Add($"{field} '{actual}' is not the current '{expected}'");
        }
    }

    private static void CompareHash(List<string> problems, string field, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{field} '{actual}' is not the current '{expected}'");
        }
    }
}
