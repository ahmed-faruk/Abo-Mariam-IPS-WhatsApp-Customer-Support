using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// Qodo findings #2 (no evidence from a partial outage), #4 (stale runs are rejected) and
/// #5 (every run covers the dataset exactly once) are enforced here, before any gate is
/// evaluated or two runs are combined.
/// </summary>
public sealed class RunCompatibilityTests
{
    private static readonly NluOutput Unfiltered = BenchmarkFixtures.NoFilterSearch();

    [Fact]
    public void A_complete_current_run_is_accepted() =>
        RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.CompleteRun("run1").WithEnvironment()]);

    [Fact]
    public void A_run_missing_a_case_is_rejected()
    {
        var executions = BenchmarkFixtures
            .AllExpectedExecutions()
            .Where(execution => execution.CaseId != "PS-001")
            .ToArray();

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.CompleteRun("run1", executions).WithEnvironment()]));

        Assert.Contains("run1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PS-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_repeating_a_case_is_rejected()
    {
        var executions = BenchmarkFixtures
            .AllExpectedExecutions()
            .Append(BenchmarkFixtures.Execution("PS-001", Unfiltered))
            .ToArray();

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.CompleteRun("run1", executions).WithEnvironment()]));

        Assert.Contains("duplicate 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PS-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_with_a_case_outside_the_dataset_is_rejected()
    {
        var executions = BenchmarkFixtures
            .AllExpectedExecutions()
            .Append(BenchmarkFixtures.Execution("ZZZ-001", Unfiltered))
            .ToArray();

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.CompleteRun("run1", executions).WithEnvironment()]));

        Assert.Contains("not in the dataset 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ZZZ-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_with_a_transport_failure_is_never_evaluated()
    {
        var executions = BenchmarkFixtures.AllExpectedExecutions();
        executions[0] = BenchmarkFixtures.Execution(
            executions[0].CaseId,
            output: null,
            schemaValid: false,
            failureReason: "transport-failure: Ollama did not answer within 20 seconds.",
            transportFailure: "Ollama did not answer within 20 seconds.");

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.CompleteRun("run1", executions).WithEnvironment()]));

        Assert.Contains("run1", exception.Message, StringComparison.Ordinal);
        Assert.Contains(executions[0].CaseId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("infrastructure-failure evidence", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dataset")]
    [InlineData("schema")]
    [InlineData("prompt")]
    [InlineData("harness")]
    public void A_mutually_matching_but_stale_run_is_rejected(string staleField)
    {
        var run1 = BenchmarkFixtures.CompleteRun("run1").WithEnvironment();
        var run2 = BenchmarkFixtures.CompleteRun("run2").WithEnvironment();

        (run1, run2) = staleField switch
        {
            "dataset" => (run1 with { DatasetSha256 = new string('1', 64) }, run2 with { DatasetSha256 = new string('1', 64) }),
            "schema" => (run1 with { SchemaSha256 = new string('2', 64) }, run2 with { SchemaSha256 = new string('2', 64) }),
            "prompt" => (run1 with { PromptVersion = "nlu-system-prompt-v2" }, run2 with { PromptVersion = "nlu-system-prompt-v2" }),
            _ => (run1 with { HarnessVersion = "issue8-nlu-benchmark-0" }, run2 with { HarnessVersion = "issue8-nlu-benchmark-0" }),
        };

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [run1, run2]));

        Assert.Contains("run1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_schema_version_is_rejected_even_when_the_hash_matches()
    {
        var run = BenchmarkFixtures.CompleteRun("run1") with { SchemaVersion = "nlu-output-v0" };

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [run.WithEnvironment()]));

        Assert.Contains("schema version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_with_different_request_settings_cannot_be_combined()
    {
        var run1 = BenchmarkFixtures.CompleteRun("run1").WithEnvironment();
        var run2 = BenchmarkFixtures.CompleteRun("run2").WithEnvironment().WithSettings(timeoutSeconds: 45);

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [run1, run2]));

        Assert.Contains("request settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("timeout seconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_measured_on_a_different_machine_cannot_be_combined()
    {
        var run1 = BenchmarkFixtures.CompleteRun("run1").WithEnvironment();
        var run2 = BenchmarkFixtures.CompleteRun("run2").WithEnvironment(architecture: "arm64");

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [run1, run2]));

        Assert.Contains("architecture", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_that_only_differ_by_collection_and_completion_timestamps_still_combine()
    {
        var run1 = BenchmarkFixtures.CompleteRun("run1").WithEnvironment();
        var run2 = BenchmarkFixtures.CompleteRun("run2")
            .WithEnvironment(collectedAtUtc: "2026-01-02T09:30:00.0000000+00:00")
            with
        {
            CompletedAtUtc = "2026-01-02T09:45:00.0000000+00:00",
        };

        RunCompatibility.Validate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run1, run2]);
    }

    [Fact]
    public void Runs_without_environment_metadata_cannot_be_combined()
    {
        var run1 = BenchmarkFixtures.CompleteRun("run1");
        var run2 = BenchmarkFixtures.CompleteRun("run2");

        var exception = Assert.Throws<BenchmarkDataException>(() => RunCompatibility.Validate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [run1, run2]));

        Assert.Contains("environment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recorded_v3_evidence_still_validates_and_keeps_its_conclusion()
    {
        var store = new RunArtifactStore(BenchmarkFixtures.Paths.ResultsDirectory);

        // The raw artifacts are gitignored, so this guard only runs where the Intel Mac kept
        // them. It never writes: it proves the recorded evidence still matches this harness.
        if (!store.Exists("v3-run1") || !store.Exists("v3-run2"))
        {
            return;
        }

        RunArtifact[] runs = [store.Load("v3-run1"), store.Load("v3-run2")];

        RunCompatibility.Validate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, runs);

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, runs);

        Assert.Equal(114, evaluation.Metrics.CaseCount);
        Assert.Equal(62.3m, evaluation.Metrics.IntentAccuracyPercent);
        Assert.Equal(100m, evaluation.Metrics.HardBudgetAccuracyPercent);
        Assert.False(evaluation.OverallPass);
        Assert.Equal("quality-fail-latency-pass", evaluation.Decision.Outcome);
    }
}
