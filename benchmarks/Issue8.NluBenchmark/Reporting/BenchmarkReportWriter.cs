using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

public sealed record ReportRunSummary
{
    public required string RunId { get; init; }

    public required string CompletedAtUtc { get; init; }

    public required int CaseCount { get; init; }

    public required decimal SchemaValidPercent { get; init; }

    public required decimal IntentAccuracyPercent { get; init; }

    public required decimal HardBudgetAccuracyPercent { get; init; }

    public required double WarmMedianMilliseconds { get; init; }

    public required double WarmP95Milliseconds { get; init; }

    public required int RetryCount { get; init; }

    public required int MalformedCount { get; init; }

    public int? PromptEvalTokens { get; init; }

    public int? EvalTokens { get; init; }

    public long? PromptEvalDurationMilliseconds { get; init; }

    public long? EvalDurationMilliseconds { get; init; }

    public long? LoadDurationMilliseconds { get; init; }
}

/// <summary>The machine-readable companion to the markdown report.</summary>
public sealed record BenchmarkReport
{
    public required string Model { get; init; }

    public required string Mode { get; init; }

    public required string GeneratedAtUtc { get; init; }

    public required string HarnessVersion { get; init; }

    public required string SourceDocument { get; init; }

    public required string DatasetVersion { get; init; }

    public required string DatasetSha256 { get; init; }

    public required string SchemaVersion { get; init; }

    public required string SchemaSha256 { get; init; }

    public required string PromptVersion { get; init; }

    public required RunSettings Settings { get; init; }

    public EnvironmentMetadata? Environment { get; init; }

    public required ReportRunSummary[] Runs { get; init; }

    public required BenchmarkMetrics Combined { get; init; }

    public required AcceptanceGate[] Gates { get; init; }

    public required bool OverallPass { get; init; }

    public required ModelDecision Decision { get; init; }

    public required RepresentativeMismatch[] RepresentativeMismatches { get; init; }
}

public sealed record ReportOutputs(string MarkdownPath, string JsonPath);

/// <summary>
/// Writes the artefact Issue #8 asks for: both run summaries, the combined metrics, each
/// v3.2 gate with its measured value, representative mismatches and the model decision.
/// </summary>
public static class BenchmarkReportWriter
{
    public const string DryRunBanner =
        "SYNTHETIC DRY RUN — NOT BENCHMARK EVIDENCE. Produced by the offline fixture gateway.";

    public static string Slug(string model)
    {
        var builder = new StringBuilder(model.Length);
        var lastWasSeparator = false;

        foreach (var character in model.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '_')
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    public static ReportOutputs Write(
        string directory,
        IReadOnlyList<RunArtifact> runs,
        BenchmarkEvaluation evaluation,
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        Func<DateTimeOffset>? clock = null)
    {
        if (runs.Count == 0)
        {
            throw new BenchmarkDataException("At least one run artifact is required to write a report.");
        }

        var report = Build(runs, evaluation, manifest, dataset, clock);
        var modeSuffix = string.Equals(report.Mode, RunModes.DryRun, StringComparison.Ordinal) ? "-dry-run" : "-intel-mac";
        var baseName = Slug(report.Model) + modeSuffix;
        Directory.CreateDirectory(directory);

        var markdownPath = Path.Combine(directory, baseName + ".md");
        var jsonPath = Path.Combine(directory, baseName + ".json");
        File.WriteAllText(markdownPath, RenderMarkdown(report));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, BenchmarkJson.Options));

        return new ReportOutputs(markdownPath, jsonPath);
    }

    public static BenchmarkReport Build(
        IReadOnlyList<RunArtifact> runs,
        BenchmarkEvaluation evaluation,
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        Func<DateTimeOffset>? clock = null) => new()
        {
            Model = runs[0].Model,
            Mode = runs[0].Mode,
            GeneratedAtUtc = (clock?.Invoke() ?? DateTimeOffset.UtcNow)
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            HarnessVersion = NluContract.HarnessVersion,
            SourceDocument = NluContract.SourceDocument,
            DatasetVersion = dataset.Version,
            DatasetSha256 = dataset.Sha256,
            SchemaVersion = manifest.Schema.Version,
            SchemaSha256 = manifest.Schema.Sha256,
            PromptVersion = manifest.Prompt.Version,
            Settings = runs[0].Settings,
            Environment = runs[0].Environment,
            Runs =
            [
                .. runs.Select(run =>
                {
                    var metrics = BenchmarkEvaluator.MetricsForRun(manifest, dataset, run);

                    return new ReportRunSummary
                    {
                        RunId = run.RunId,
                        CompletedAtUtc = run.CompletedAtUtc,
                        CaseCount = metrics.CaseCount,
                        SchemaValidPercent = metrics.SchemaValidPercent,
                        IntentAccuracyPercent = metrics.IntentAccuracyPercent,
                        HardBudgetAccuracyPercent = metrics.HardBudgetAccuracyPercent,
                        WarmMedianMilliseconds = metrics.WarmMedianMilliseconds,
                        WarmP95Milliseconds = metrics.WarmP95Milliseconds,
                        RetryCount = metrics.RetryCount,
                        MalformedCount = metrics.MalformedCount,
                        PromptEvalTokens = SumTokenCounts(run, timing => timing.PromptEvalCount),
                        EvalTokens = SumTokenCounts(run, timing => timing.EvalCount),
                        PromptEvalDurationMilliseconds = SumDurations(run, timing => timing.PromptEvalDurationNanoseconds),
                        EvalDurationMilliseconds = SumDurations(run, timing => timing.EvalDurationNanoseconds),
                        LoadDurationMilliseconds = SumDurations(run, timing => timing.LoadDurationNanoseconds),
                    };
                }),
            ],
            Combined = evaluation.Metrics,
            Gates = evaluation.Gates,
            OverallPass = evaluation.OverallPass,
            Decision = evaluation.Decision,
            RepresentativeMismatches = evaluation.Metrics.RepresentativeMismatches,
        };

    public static string RenderMarkdown(BenchmarkReport report)
    {
        var builder = new StringBuilder();

        builder.Append("# NLU benchmark report — ").Append(report.Model).Append('\n').Append('\n');

        if (string.Equals(report.Mode, RunModes.DryRun, StringComparison.Ordinal))
        {
            builder.Append("> ").Append(DryRunBanner).Append('\n').Append('\n');
        }

        builder.Append("| field | value |\n| --- | --- |\n");
        AppendRow(builder, "Model", report.Model);
        AppendRow(builder, "Mode", report.Mode);
        AppendRow(builder, "Generated (UTC)", report.GeneratedAtUtc);
        AppendRow(builder, "Harness", report.HarnessVersion);
        AppendRow(builder, "Source of truth", report.SourceDocument);
        AppendRow(builder, "Dataset", $"{report.DatasetVersion} · sha256 {report.DatasetSha256}");
        AppendRow(builder, "Schema", $"{report.SchemaVersion} · sha256 {report.SchemaSha256}");
        AppendRow(builder, "Prompt", report.PromptVersion);
        AppendRow(
            builder,
            "Temperature / context / timeout / retry",
            $"{report.Settings.Temperature} / {report.Settings.ContextTokens} tokens / "
            + $"{report.Settings.TimeoutSeconds} s / {report.Settings.RetryPolicy}");

        builder.Append('\n').Append("## Environment\n\n| field | value |\n| --- | --- |\n");

        if (report.Environment is { } environment)
        {
            AppendRow(builder, "macOS", environment.OsVersion);
            AppendRow(builder, "Architecture", environment.Architecture);
            AppendRow(builder, "CPU", environment.Cpu);
            AppendRow(builder, "Memory", EnvironmentMetadataCollector.DescribeMemory(environment.MemoryBytes));
            AppendRow(builder, "Ollama", environment.OllamaVersion ?? "unknown");
            AppendRow(builder, "Collected (UTC)", environment.CollectedAtUtc);
        }
        else
        {
            AppendRow(builder, "Environment", "not captured");
        }

        builder.Append('\n').Append("## Runs\n\n");
        builder.Append("| run | completed (UTC) | cases | schema-valid % | intent % | hard-budget % | median s | p95 s | retries | malformed |\n");
        builder.Append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");

        foreach (var run in report.Runs)
        {
            builder.Append("| ").Append(run.RunId)
                .Append(" | ").Append(run.CompletedAtUtc)
                .Append(" | ").Append(run.CaseCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(BenchmarkEvaluator.Format(run.SchemaValidPercent))
                .Append(" | ").Append(BenchmarkEvaluator.Format(run.IntentAccuracyPercent))
                .Append(" | ").Append(BenchmarkEvaluator.Format(run.HardBudgetAccuracyPercent))
                .Append(" | ").Append(BenchmarkEvaluator.FormatSeconds(run.WarmMedianMilliseconds))
                .Append(" | ").Append(BenchmarkEvaluator.FormatSeconds(run.WarmP95Milliseconds))
                .Append(" | ").Append(run.RetryCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(run.MalformedCount.ToString(CultureInfo.InvariantCulture))
                .Append(" |\n");
        }

        builder.Append('\n').Append("## Combined gates (docs/PLAN.md section 13.3)\n\n");
        builder.Append("| gate | threshold | measured | result |\n| --- | --- | --- | --- |\n");

        foreach (var gate in report.Gates)
        {
            builder.Append("| ").Append(gate.Name)
                .Append(" | ").Append(gate.Threshold)
                .Append(" | ").Append(gate.Measured)
                .Append(" | ").Append(gate.Passed ? "PASS" : "FAIL")
                .Append(" |\n");
        }

        builder.Append("\nOverall: ").Append(report.OverallPass ? "**PASS**" : "**FAIL**").Append("\n");
        builder.Append("\nGate metrics use the ").Append(report.Combined.CaseCount.ToString(CultureInfo.InvariantCulture))
            .Append(" gating cases; ")
            .Append(report.Combined.ObservationalCaseCount.ToString(CultureInfo.InvariantCulture))
            .Append(" observational case(s) are reported but excluded from every gate.\n");

        builder.Append("\n## Field statistics\n\n| field | matched | compared | % |\n| --- | --- | --- | --- |\n");

        foreach (var field in report.Combined.FieldStats)
        {
            builder.Append("| ").Append(field.Field)
                .Append(" | ").Append(field.Matched.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(field.Compared.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(BenchmarkEvaluator.Format(field.Percent))
                .Append(" |\n");
        }

        builder.Append("\n## Retry and malformed output\n\n")
            .Append("- retries (gating cases): ").Append(report.Combined.RetryCount.ToString(CultureInfo.InvariantCulture))
            .Append(" (").Append(BenchmarkEvaluator.Format(report.Combined.RetryRatePercent)).Append("%)\n")
            .Append("- malformed after one retry: ").Append(report.Combined.MalformedCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

        builder.Append("\n## Token metrics (Ollama-reported)\n\n");
        builder.Append("| run | prompt-eval tokens | output tokens | prompt-eval ms | output ms | load ms |\n");
        builder.Append("| --- | --- | --- | --- | --- | --- |\n");

        foreach (var run in report.Runs)
        {
            builder.Append("| ").Append(run.RunId)
                .Append(" | ").Append(RenderOptional(run.PromptEvalTokens))
                .Append(" | ").Append(RenderOptional(run.EvalTokens))
                .Append(" | ").Append(RenderOptional(run.PromptEvalDurationMilliseconds))
                .Append(" | ").Append(RenderOptional(run.EvalDurationMilliseconds))
                .Append(" | ").Append(RenderOptional(run.LoadDurationMilliseconds))
                .Append(" |\n");
        }

        builder.Append("\nValues come from Ollama's own timing fields; `n/a` means Ollama did not return them. "
            + "The harness never manufactures a token count.\n");

        builder.Append("\n## Representative mismatches\n\n");

        if (report.RepresentativeMismatches.Length == 0)
        {
            builder.Append("None: every gating case matched its documented expectation.\n");
        }
        else
        {
            builder.Append("| case | input | reason |\n| --- | --- | --- |\n");

            foreach (var mismatch in report.RepresentativeMismatches)
            {
                builder.Append("| ").Append(mismatch.CaseId)
                    .Append(" | ").Append(mismatch.Input)
                    .Append(" | ").Append(mismatch.Reason)
                    .Append(" |\n");
            }
        }

        builder.Append("\n## Model decision (input for Issue #9)\n\n")
            .Append("- outcome: `").Append(report.Decision.Outcome).Append("`\n")
            .Append("- recommendation: ").Append(report.Decision.Recommendation).Append('\n');

        builder.Append("\n## Commercial-fact invariant\n\n")
            .Append("The harness only compares structured NLU output; it never writes model output to PostgreSQL. "
                + "docs/PLAN.md section 13.3 requires that no model output path can alter SQL commercial facts, "
                + "which the production architecture enforces by reloading Catalog/Storefront facts before rendering.\n");

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string field, string value) =>
        builder.Append("| ").Append(field).Append(" | ").Append(value).Append(" |\n");

    private static string RenderOptional(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static string RenderOptional(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static int? SumTokenCounts(RunArtifact run, Func<NluTransportTiming, int?> selector)
    {
        var counts = Timings(run).Select(selector).OfType<int>().ToArray();

        // An Ollama response that omits a token count is not a measured zero: report n/a
        // unless at least one reply actually returned a count.
        return counts.Length == 0 ? null : counts.Sum();
    }

    private static long? SumDurations(RunArtifact run, Func<NluTransportTiming, long?> selector)
    {
        var values = Timings(run).Select(selector).OfType<long>().ToArray();

        return values.Length == 0 ? null : values.Sum() / 1_000_000;
    }

    private static List<NluTransportTiming> Timings(RunArtifact run) =>
        [.. run.Cases.SelectMany(testCase => testCase.Attempts).Select(attempt => attempt.Timing).OfType<NluTransportTiming>()];
}
