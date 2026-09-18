using System.Text.Json;
using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// The report is the Issue #8 artefact: both runs, the combined metrics, every gate with its
/// measured value, and the decision input for Issue #9.
/// </summary>
public sealed class BenchmarkReportWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"issue8-report-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Report_contains_both_runs_the_combined_gates_and_the_decision()
    {
        var expected = BenchmarkFixtures.Expected("PS-001");
        var hardBudget = BenchmarkFixtures.Expected("BUD-HARD-001");
        var run1 = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", expected, milliseconds: 1000),
            BenchmarkFixtures.Execution("BUD-HARD-001", hardBudget, milliseconds: 1500));
        var run2 = BenchmarkFixtures.Run(
            "run2",
            BenchmarkFixtures.Execution("PS-001", expected, milliseconds: 2000),
            BenchmarkFixtures.Execution("BUD-HARD-001", hardBudget, milliseconds: 2500));
        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run1, run2]);

        var outputs = BenchmarkReportWriter.Write(
            _directory,
            [run1, run2],
            evaluation,
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            () => DateTimeOffset.Parse("2026-02-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(File.Exists(outputs.MarkdownPath));
        Assert.True(File.Exists(outputs.JsonPath));
        Assert.EndsWith("test-model-intel-mac.md", outputs.MarkdownPath, StringComparison.Ordinal);

        var markdown = File.ReadAllText(outputs.MarkdownPath);

        Assert.Contains("| run1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| run2 |", markdown, StringComparison.Ordinal);
        Assert.Contains("docs/PLAN.md section 13.3", markdown, StringComparison.Ordinal);
        Assert.Contains("Intent accuracy", markdown, StringComparison.Ordinal);
        Assert.Contains("Hard-budget classification", markdown, StringComparison.Ordinal);
        Assert.Contains("Structured schema success after one retry", markdown, StringComparison.Ordinal);
        Assert.Contains("Warm median latency", markdown, StringComparison.Ordinal);
        Assert.Contains("Warm p95 latency", markdown, StringComparison.Ordinal);
        Assert.Contains("Overall: **PASS**", markdown, StringComparison.Ordinal);
        Assert.Contains("no model output path can alter SQL commercial facts", markdown, StringComparison.Ordinal);
        Assert.Contains(BenchmarkFixtures.Dataset.Sha256, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(BenchmarkReportWriter.DryRunBanner, markdown, StringComparison.Ordinal);

        var report = JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(outputs.JsonPath), BenchmarkJson.Options)!;

        Assert.Equal("test-model", report.Model);
        Assert.Equal(RunModes.Live, report.Mode);
        Assert.Equal("2026-02-01T10:00:00.0000000+00:00", report.GeneratedAtUtc);
        Assert.Equal(2, report.Runs.Length);
        Assert.Equal(BenchmarkFixtures.Dataset.Sha256, report.DatasetSha256);
        Assert.True(report.OverallPass);
        Assert.Equal("quality-and-latency-pass", report.Decision.Outcome);
    }

    [Fact]
    public void A_failing_report_says_fail_and_names_the_measured_values()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PRICE-001", BenchmarkFixtures.Expected("PS-001")));
        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        var outputs = BenchmarkReportWriter.Write(
            _directory,
            [run],
            evaluation,
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset);

        var markdown = File.ReadAllText(outputs.MarkdownPath);

        Assert.Contains("Overall: **FAIL**", markdown, StringComparison.Ordinal);
        Assert.Contains("| FAIL |", markdown, StringComparison.Ordinal);
        Assert.Contains("PRICE-001", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Dry_run_reports_are_named_and_banner_marked_as_synthetic()
    {
        var run = BenchmarkFixtures.Run("dry-run-1", BenchmarkFixtures.Execution("PS-001", BenchmarkFixtures.Expected("PS-001")));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);
        var outputs = BenchmarkReportWriter.Write(
            _directory,
            [run with { Mode = RunModes.DryRun }],
            evaluation,
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset);

        Assert.EndsWith("test-model-dry-run.md", outputs.MarkdownPath, StringComparison.Ordinal);
        Assert.Contains(BenchmarkReportWriter.DryRunBanner, File.ReadAllText(outputs.MarkdownPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("qwen3.5:2b-q4_K_M", "qwen3.5-2b-q4_k_m")]
    [InlineData("qwen3:1.7b", "qwen3-1.7b")]
    [InlineData("Test Model", "test-model")]
    public void Model_tags_become_stable_file_names(string model, string expected) =>
        Assert.Equal(expected, BenchmarkReportWriter.Slug(model));

    [Fact]
    public void Ollama_token_metrics_are_reported_when_ollama_returns_them()
    {
        var timing = new NluTransportTiming
        {
            TotalDurationNanoseconds = 3_000_000_000,
            LoadDurationNanoseconds = 1_000_000_000,
            PromptEvalCount = 420,
            PromptEvalDurationNanoseconds = 250_000_000,
            EvalCount = 35,
            EvalDurationNanoseconds = 1_500_000_000,
        };
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", BenchmarkFixtures.Expected("PS-001"), timing: timing),
            BenchmarkFixtures.Execution("PS-002", BenchmarkFixtures.Expected("PS-002"), timing: timing));
        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        var outputs = BenchmarkReportWriter.Write(
            _directory,
            [run],
            evaluation,
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset);
        var report = JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(outputs.JsonPath), BenchmarkJson.Options)!;
        var summary = Assert.Single(report.Runs);

        Assert.Equal(840, summary.PromptEvalTokens);
        Assert.Equal(70, summary.EvalTokens);
        Assert.Equal(500, summary.PromptEvalDurationMilliseconds);
        Assert.Equal(3000, summary.EvalDurationMilliseconds);
        Assert.Equal(2000, summary.LoadDurationMilliseconds);

        var markdown = File.ReadAllText(outputs.MarkdownPath);

        Assert.Contains("## Token metrics (Ollama-reported)", markdown, StringComparison.Ordinal);
        Assert.Contains("| run1 | 840 | 70 | 500 | 3000 | 2000 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_ollama_timings_are_reported_as_not_available()
    {
        var run = BenchmarkFixtures.Run("run1", BenchmarkFixtures.Execution("PS-001", BenchmarkFixtures.Expected("PS-001")));
        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        var outputs = BenchmarkReportWriter.Write(
            _directory,
            [run],
            evaluation,
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset);

        Assert.Contains("| run1 | n/a | n/a | n/a | n/a | n/a |", File.ReadAllText(outputs.MarkdownPath), StringComparison.Ordinal);
    }
}
