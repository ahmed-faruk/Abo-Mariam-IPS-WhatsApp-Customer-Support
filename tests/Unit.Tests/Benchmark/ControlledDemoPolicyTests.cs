using System.Security.Cryptography;
using System.Text.Json;
using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// Issue #9 freezes a Controlled Demo Candidate on top of the immutable Issue #8 evidence. These
/// checks keep the frozen configuration, the recorded general-benchmark failure, the versioned demo
/// gate and the pilot/production wording from drifting apart. They assert documented statements;
/// they never compare a whole document byte for byte.
/// </summary>
public sealed class ControlledDemoPolicyTests
{
    private const string ControlledDemoModel = "qwen3.5:2b-q4_K_M";
    private const string ControlledDemoBaseUrl = "http://127.0.0.1:11434";
    private const string RetryPolicy = "one-retry-maximum";

    // The Issue #8 reproducibility inputs, frozen: changing them is a new benchmark, not an edit.
    private const string DatasetSha256 = "a00232ad824548eb806ac0a79141f2d9f12f0e70f4b0b962b283f838a6c3570b";
    private const string SchemaSha256 = "fb9eacee28dcf31f6438fbe63092a8b48abb42cf5c592f4edd06874b2f1d4302";
    private const string PromptVersion = "nlu-system-prompt-v3";

    private const string PlanDocument = "docs/PLAN.md";
    private const string TechnicalDocument = "docs/TECHNICAL.md";
    private const string GateDocument = "docs/demo/DEMO-CRITICAL-GATE-v1.md";

    private const string ReportJsonPath =
        "benchmarks/Issue8.NluBenchmark/reports/qwen3.5-2b-q4_k_m-intel-mac.json";

    private const string ReportMarkdownPath =
        "benchmarks/Issue8.NluBenchmark/reports/qwen3.5-2b-q4_k_m-intel-mac.md";

    /// <summary>
    /// The documents that carry the AI acceptance policy. The measured report is checked separately
    /// because it is generated evidence rather than policy.
    /// </summary>
    private static readonly string[] PolicyDocuments = [PlanDocument, TechnicalDocument, GateDocument];

    private static readonly (string Id, string Name)[] GateScenarios =
    [
        ("DEMO-01", "Brand + size search"),
        ("DEMO-02", "Follow-up filters"),
        ("DEMO-03", "Soft budget"),
        ("DEMO-04", "Hard budget (CRITICAL)"),
        ("DEMO-05", "Multi-turn reference + price"),
        ("DEMO-06", "Live price change"),
        ("DEMO-07", "Live stock/availability change"),
        ("DEMO-08", "Business FAQ"),
        ("DEMO-09", "Live FAQ change"),
        ("DEMO-10", "Human takeover"),
        ("DEMO-11", "Admin transcript"),
        ("DEMO-12", "Duplicate inbound"),
        ("DEMO-13", "Ollama unavailable"),
    ];

    [Fact]
    public void Frozen_candidate_matches_the_committed_manifest()
    {
        var candidate = BenchmarkFixtures.Manifest.DefaultCandidate;

        Assert.Equal(ControlledDemoModel, candidate.Model);
        Assert.Equal(ControlledDemoBaseUrl, candidate.BaseUrl);
        Assert.Equal(20, candidate.TimeoutSeconds);
        Assert.Equal(0, candidate.Temperature);
        Assert.Equal(4096, candidate.ContextTokens);
        Assert.Equal(RetryPolicy, candidate.RetryPolicy);
    }

    [Fact]
    public void Documents_record_the_frozen_request_shape()
    {
        foreach (var relativePath in new[] { TechnicalDocument, GateDocument })
        {
            var text = RepoFile(relativePath);

            Assert.Contains(ControlledDemoModel, text, StringComparison.Ordinal);
            Assert.Contains("127.0.0.1:11434", text, StringComparison.Ordinal);
        }

        var technical = RepoFile(TechnicalDocument);

        Assert.Contains("stream: false", technical, StringComparison.Ordinal);
        Assert.Contains("think: false", technical, StringComparison.Ordinal);
        Assert.Contains("one corrective retry maximum", technical, StringComparison.Ordinal);
        Assert.Contains("pre-warm", technical, StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_dataset_and_schema_bytes_still_match_the_frozen_hashes()
    {
        Assert.Equal(PromptVersion, BenchmarkFixtures.Manifest.Prompt.Version);
        Assert.Equal(PromptVersion, NluContract.PromptVersion);
        Assert.Equal(DatasetSha256, Sha256Of("benchmarks/Issue8.NluBenchmark/Data/v1/cases.jsonl"));
        Assert.Equal(SchemaSha256, Sha256Of("benchmarks/Issue8.NluBenchmark/schemas/nlu-output.schema.json"));
        Assert.Equal(DatasetSha256, BenchmarkFixtures.Manifest.Dataset.Sha256);
        Assert.Equal(SchemaSha256, BenchmarkFixtures.Manifest.Schema.Sha256);
    }

    [Fact]
    public void General_gate_thresholds_are_unchanged()
    {
        var gates = BenchmarkFixtures.Manifest.AcceptanceGates;

        Assert.Equal(90m, gates.IntentAccuracyPercent);
        Assert.Equal(100m, gates.HardBudgetAccuracyPercent);
        Assert.Equal(98m, gates.SchemaSuccessPercent);
        Assert.Equal(8m, gates.WarmMedianSeconds);
        Assert.Equal(12m, gates.WarmP95Seconds);
        Assert.Equal(60, BenchmarkFixtures.Manifest.Dataset.CaseCount);
    }

    [Fact]
    public void Issue8_report_still_records_the_general_failure()
    {
        using var report = JsonDocument.Parse(RepoFile(ReportJsonPath));
        var root = report.RootElement;

        Assert.Equal(ControlledDemoModel, root.GetProperty("model").GetString());
        Assert.Equal(DatasetSha256, root.GetProperty("datasetSha256").GetString());
        Assert.Equal(SchemaSha256, root.GetProperty("schemaSha256").GetString());
        Assert.Equal(PromptVersion, root.GetProperty("promptVersion").GetString());
        Assert.False(root.GetProperty("overallPass").GetBoolean());
        Assert.Equal("quality-fail-latency-pass", root.GetProperty("decision").GetProperty("outcome").GetString());
        Assert.Equal([57, 57], root.GetProperty("runs").EnumerateArray()
            .Select(run => run.GetProperty("caseCount").GetInt32()).ToArray());

        var gates = root.GetProperty("issue8Outcome").GetProperty("combinedGates").EnumerateArray().ToArray();

        Assert.Equal(
            ["62.3%", "100% (10/10)", "100%", "6.41 s", "8.17 s"],
            gates.Select(gate => gate.GetProperty("measured").GetString() ?? string.Empty).ToArray());
        Assert.Equal(
            [false, true, true, true, true],
            gates.Select(gate => gate.GetProperty("passed").GetBoolean()).ToArray());
    }

    [Fact]
    public void Issue8_report_still_records_the_4b_warmup_blocker()
    {
        var markdown = RepoFile(ReportMarkdownPath);

        Assert.Contains("qwen3.5:4b-q4_K_M", markdown, StringComparison.Ordinal);
        Assert.Contains("warm-up failed", markdown, StringComparison.Ordinal);
        Assert.Contains("no measured", markdown, StringComparison.Ordinal);
        Assert.Contains("qwen3:1.7b", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlanDocument)]
    [InlineData(TechnicalDocument)]
    [InlineData(GateDocument)]
    public void Every_measured_intent_score_is_stated_as_a_failure(string relativePath)
    {
        var lines = RepoLines(relativePath);
        var mentions = Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].Contains("62.3", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(mentions);

        foreach (var index in mentions)
        {
            var window = lines[Math.Max(0, index - 2)..Math.Min(lines.Length, index + 3)];

            Assert.True(
                window.Any(line => line.Contains("FAIL", StringComparison.Ordinal)),
                $"{relativePath}:{index + 1} states the measured 62.3% score without marking it a failure.");
        }
    }

    [Theory]
    [InlineData(PlanDocument)]
    [InlineData(TechnicalDocument)]
    [InlineData(GateDocument)]
    public void Controlled_demo_status_is_separated_from_pilot_and_production(string relativePath)
    {
        var text = RepoFile(relativePath);

        Assert.Contains("Controlled Demo Candidate", text, StringComparison.Ordinal);
        Assert.Contains("pilot/production", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("90%", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlanDocument)]
    [InlineData(TechnicalDocument)]
    public void General_intent_threshold_stays_tied_to_pilot_production(string relativePath)
    {
        var lines = RepoLines(relativePath);
        var thresholds = Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].Contains("90%", StringComparison.Ordinal))
            .ToArray();
        var promotions = Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].Contains("pilot/production", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(thresholds);
        Assert.NotEmpty(promotions);
        Assert.True(
            promotions.Any(promotion => thresholds.Any(threshold => Math.Abs(promotion - threshold) <= 4)),
            $"{relativePath} no longer ties the 90% general threshold to pilot/production acceptance.");
    }

    [Fact]
    public void Demo_gate_versions_every_required_scenario()
    {
        var lines = RepoLines(GateDocument);

        Assert.Contains("v1", lines[0], StringComparison.Ordinal);

        foreach (var (id, name) in GateScenarios)
        {
            Assert.True(
                lines.Any(line => line.Contains(id, StringComparison.Ordinal) && line.Contains(name, StringComparison.Ordinal)),
                $"{GateDocument} no longer defines {id} ({name}).");
        }
    }

    [Fact]
    public void Demo_gate_defines_thirteen_required_scenarios_and_no_counted_fourteenth()
    {
        Assert.Equal(13, GateScenarios.Length);

        var text = RepoFile(GateDocument);

        Assert.Contains("13 required executable scenarios per run", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEMO-14", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Demo_gate_acceptance_requires_two_consecutive_complete_runs()
    {
        var text = RepoFile(GateDocument);

        Assert.Contains("100% of the 13 required scenarios per run", text, StringComparison.Ordinal);
        Assert.Contains("Run 1: DEMO-01 … DEMO-13 must ALL PASS", text, StringComparison.Ordinal);
        Assert.Contains("Run 2: DEMO-01 … DEMO-13 must ALL PASS", text, StringComparison.Ordinal);
        Assert.Contains("(13/13)", text, StringComparison.Ordinal);
        Assert.Contains("Anything less than 100% in either run", text, StringComparison.Ordinal);
        Assert.Contains("never averaged", text, StringComparison.Ordinal);
        Assert.Contains("never re-run selectively", text, StringComparison.Ordinal);
        Assert.Contains("NEW complete run from DEMO-01 through", text, StringComparison.Ordinal);
        Assert.Contains("Controlled Client Demo Gate: PASS", text, StringComparison.Ordinal);
        Assert.Contains("Controlled Client Demo Gate: FAIL", text, StringComparison.Ordinal);
        Assert.Contains("not pilot/production acceptance", text, StringComparison.Ordinal);
    }

    private static string RepoFile(string relativePath)
    {
        var path = Path.Combine(
            BenchmarkFixtures.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"Expected {relativePath} to exist in the repository.");
        return File.ReadAllText(path);
    }

    private static string[] RepoLines(string relativePath) =>
        [.. RepoFile(relativePath).Split('\n').Select(line => line.TrimEnd('\r'))];

    private static string Sha256Of(string relativePath)
    {
        var path = Path.Combine(
            BenchmarkFixtures.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
