using System.Globalization;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

public sealed record FieldStat
{
    public required string Field { get; init; }

    public required int Matched { get; init; }

    public required int Compared { get; init; }

    public decimal Percent => Compared == 0 ? 0 : decimal.Round(Matched * 100m / Compared, 1);
}

public sealed record RepresentativeMismatch
{
    public required string CaseId { get; init; }

    public required string Input { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// One evaluated case. Everything here is derived from the committed dataset and the run
/// artifact, so a report can be regenerated without touching Ollama.
/// </summary>
public sealed record CaseEvaluation
{
    public required string CaseId { get; init; }

    public required string Input { get; init; }

    public required bool Gating { get; init; }

    public required bool HardBudgetCase { get; init; }

    public required string ExpectedIntent { get; init; }

    public required string? ActualIntent { get; init; }

    public required bool SchemaValid { get; init; }

    public required bool Retried { get; init; }

    public required bool IntentCorrect { get; init; }

    public required bool HardBudgetCorrect { get; init; }

    public required long FinalAttemptMilliseconds { get; init; }

    public required long TotalMilliseconds { get; init; }

    public required string[] MatchedFields { get; init; }

    public required FieldMismatch[] Mismatches { get; init; }

    public string? FailureReason { get; init; }
}

public sealed record BenchmarkMetrics
{
    public required int CaseCount { get; init; }

    public required int SchemaValidCount { get; init; }

    public required decimal SchemaValidPercent { get; init; }

    public required int IntentCorrectCount { get; init; }

    public required decimal IntentAccuracyPercent { get; init; }

    public required int HardBudgetCaseCount { get; init; }

    public required int HardBudgetCorrectCount { get; init; }

    public required decimal HardBudgetAccuracyPercent { get; init; }

    public required int RetryCount { get; init; }

    public required decimal RetryRatePercent { get; init; }

    public required int MalformedCount { get; init; }

    public required int ObservationalCaseCount { get; init; }

    public required double WarmMedianMilliseconds { get; init; }

    public required double WarmP95Milliseconds { get; init; }

    public required FieldStat[] FieldStats { get; init; }

    public required RepresentativeMismatch[] RepresentativeMismatches { get; init; }
}

public sealed record AcceptanceGate
{
    public required string Name { get; init; }

    public required string Source { get; init; }

    public required string Threshold { get; init; }

    public required string Measured { get; init; }

    public required bool Passed { get; init; }
}

public sealed record ModelDecision
{
    public required string Outcome { get; init; }

    public required string Recommendation { get; init; }
}

public sealed record BenchmarkEvaluation
{
    public required BenchmarkMetrics Metrics { get; init; }

    public required AcceptanceGate[] Gates { get; init; }

    public required bool OverallPass { get; init; }

    public required ModelDecision Decision { get; init; }
}

/// <summary>
/// Turns run artifacts into the PLAN section 13.3 gates. Gate metrics use gating cases only;
/// observational cases (where the source of truth defines no structured answer) are counted
/// separately and never move a gate.
/// </summary>
public static class BenchmarkEvaluator
{
    private const int RepresentativeMismatchLimit = 12;

    public static BenchmarkEvaluation Evaluate(
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        IReadOnlyList<RunArtifact> runs)
    {
        var cases = EvaluateCases(dataset, runs);
        var metrics = ComputeMetrics(cases, manifest);
        var gates = ComputeGates(metrics, manifest.AcceptanceGates);
        var qualityPass = gates.Where(gate => gate.Source.StartsWith("quality", StringComparison.Ordinal)).All(gate => gate.Passed);
        var latencyPass = gates.Where(gate => gate.Source.StartsWith("latency", StringComparison.Ordinal)).All(gate => gate.Passed);

        return new BenchmarkEvaluation
        {
            Metrics = metrics,
            Gates = gates,
            OverallPass = gates.All(gate => gate.Passed),
            Decision = Decide(runs.Count > 0 ? runs[0].Model : manifest.DefaultCandidate.Model, qualityPass, latencyPass),
        };
    }

    public static IReadOnlyList<CaseEvaluation> EvaluateCases(
        BenchmarkDataset dataset,
        IReadOnlyList<RunArtifact> runs)
    {
        var caseById = dataset.Cases.ToDictionary(testCase => testCase.Id, StringComparer.Ordinal);
        var evaluations = new List<CaseEvaluation>();

        foreach (var run in runs)
        {
            foreach (var record in run.Cases)
            {
                if (!caseById.TryGetValue(record.CaseId, out var testCase))
                {
                    throw new BenchmarkDataException(
                        $"Run {run.RunId} contains case '{record.CaseId}' which is not in the dataset.");
                }

                var comparison = NluOutputComparer.Compare(testCase.Expected, record.Output);

                evaluations.Add(new CaseEvaluation
                {
                    CaseId = testCase.Id,
                    Input = testCase.Input,
                    Gating = testCase.Gating,
                    HardBudgetCase = testCase.HardBudgetCase,
                    ExpectedIntent = testCase.Expected.Intent ?? string.Empty,
                    ActualIntent = record.Output?.Intent,
                    SchemaValid = record.SchemaValid,
                    Retried = record.Retried,
                    IntentCorrect = IsIntentCorrect(testCase.Expected, record.Output),
                    HardBudgetCorrect = IsHardBudgetCorrect(testCase.Expected, record.Output),
                    FinalAttemptMilliseconds = record.FinalAttemptMilliseconds,
                    TotalMilliseconds = record.TotalMilliseconds,
                    MatchedFields = comparison.MatchedFields,
                    Mismatches = comparison.Mismatches,
                    FailureReason = record.FailureReason,
                });
            }
        }

        return evaluations;
    }

    /// <summary>Metrics for one run on its own, used for the per-run summary rows.</summary>
    public static BenchmarkMetrics MetricsForRun(
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        RunArtifact run) =>
        ComputeMetrics(EvaluateCases(dataset, [run]), manifest);

    /// <summary>
    /// A dedicated hard-budget case is correct only when the reply classifies the budget as
    /// Hard and the resolved ceiling (budgetTarget, or budgetMax when the target is empty)
    /// equals the authored ceiling.
    /// </summary>
    public static bool IsHardBudgetCorrect(NluOutput expected, NluOutput? actual)
    {
        if (actual is null || !string.Equals(actual.BudgetType, "Hard", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resolvedCeiling = actual.BudgetTarget ?? actual.BudgetMax;
        return resolvedCeiling is not null && resolvedCeiling == expected.BudgetTarget;
    }

    public static bool IsIntentCorrect(NluOutput expected, NluOutput? actual) =>
        string.Equals(
            NluOutputComparer.Canonical(expected.Intent),
            NluOutputComparer.Canonical(actual?.Intent),
            StringComparison.Ordinal);

    private static BenchmarkMetrics ComputeMetrics(
        IReadOnlyList<CaseEvaluation> evaluations,
        BenchmarkManifest manifest)
    {
        var gating = evaluations.Where(evaluation => evaluation.Gating).ToList();
        var observational = evaluations.Count - gating.Count;
        var latencies = gating.Select(evaluation => (double)evaluation.FinalAttemptMilliseconds).ToArray();
        var fieldStats = NluContract.Fields
            .Select(field => new FieldStat
            {
                Field = field,
                Matched = gating.Count(evaluation => evaluation.MatchedFields.Contains(field, StringComparer.Ordinal)),
                Compared = gating.Count,
            })
            .ToArray();
        var mismatches = gating
            .Where(evaluation => !evaluation.SchemaValid || evaluation.Mismatches.Length > 0)
            .Select(evaluation => new RepresentativeMismatch
            {
                CaseId = evaluation.CaseId,
                Input = evaluation.Input,
                Reason = !evaluation.SchemaValid
                    ? evaluation.FailureReason ?? "schema-failure"
                    : string.Join("; ", evaluation.Mismatches.Select(mismatch => mismatch.Describe())),
            })
            .Take(RepresentativeMismatchLimit)
            .ToArray();

        var hardBudgetCases = gating.Where(evaluation => evaluation.HardBudgetCase).ToList();

        return new BenchmarkMetrics
        {
            CaseCount = gating.Count,
            SchemaValidCount = gating.Count(evaluation => evaluation.SchemaValid),
            SchemaValidPercent = Percent(gating.Count(evaluation => evaluation.SchemaValid), gating.Count),
            IntentCorrectCount = gating.Count(evaluation => evaluation.IntentCorrect),
            IntentAccuracyPercent = Percent(gating.Count(evaluation => evaluation.IntentCorrect), gating.Count),
            HardBudgetCaseCount = hardBudgetCases.Count,
            HardBudgetCorrectCount = hardBudgetCases.Count(evaluation => evaluation.HardBudgetCorrect),
            HardBudgetAccuracyPercent = Percent(
                hardBudgetCases.Count(evaluation => evaluation.HardBudgetCorrect),
                hardBudgetCases.Count),
            RetryCount = gating.Count(evaluation => evaluation.Retried),
            RetryRatePercent = Percent(gating.Count(evaluation => evaluation.Retried), gating.Count),
            MalformedCount = gating.Count(evaluation => !evaluation.SchemaValid),
            ObservationalCaseCount = observational,
            WarmMedianMilliseconds = latencies.Length == 0 ? 0 : PercentileCalculator.Percentile(latencies, 50),
            WarmP95Milliseconds = latencies.Length == 0 ? 0 : PercentileCalculator.Percentile(latencies, 95),
            FieldStats = fieldStats,
            RepresentativeMismatches = mismatches,
        };
    }

    private static AcceptanceGate[] ComputeGates(BenchmarkMetrics metrics, BenchmarkManifest.AcceptanceGateThresholds thresholds)
    {
        var source = "docs/PLAN.md section 13.3";

        AcceptanceGate[] gates =
        [
            new AcceptanceGate
            {
                Name = "Intent accuracy",
                Source = "quality",
                Threshold = $">= {Format(thresholds.IntentAccuracyPercent)}%",
                Measured = $"{Format(metrics.IntentAccuracyPercent)}%",
                Passed = metrics.IntentAccuracyPercent >= thresholds.IntentAccuracyPercent,
            },
            new AcceptanceGate
            {
                Name = "Hard-budget classification",
                Source = "quality",
                Threshold = $"= {Format(thresholds.HardBudgetAccuracyPercent)}% on dedicated hard-budget cases",
                Measured = $"{Format(metrics.HardBudgetAccuracyPercent)}% "
                    + $"({metrics.HardBudgetCorrectCount}/{metrics.HardBudgetCaseCount})",
                Passed = metrics.HardBudgetCaseCount > 0
                    && metrics.HardBudgetAccuracyPercent >= thresholds.HardBudgetAccuracyPercent,
            },
            new AcceptanceGate
            {
                Name = "Structured schema success after one retry",
                Source = "quality",
                Threshold = $">= {Format(thresholds.SchemaSuccessPercent)}%",
                Measured = $"{Format(metrics.SchemaValidPercent)}%",
                Passed = metrics.SchemaValidPercent >= thresholds.SchemaSuccessPercent,
            },
            new AcceptanceGate
            {
                Name = "Warm median latency",
                Source = "latency",
                Threshold = $"{Format(thresholds.WarmMedianSeconds)} s",
                Measured = $"{FormatSeconds(metrics.WarmMedianMilliseconds)} s",
                Passed = metrics.WarmMedianMilliseconds <= (double)thresholds.WarmMedianSeconds * 1000,
            },
            new AcceptanceGate
            {
                Name = "Warm p95 latency",
                Source = "latency",
                Threshold = $"{Format(thresholds.WarmP95Seconds)} s",
                Measured = $"{FormatSeconds(metrics.WarmP95Milliseconds)} s",
                Passed = metrics.WarmP95Milliseconds <= (double)thresholds.WarmP95Seconds * 1000,
            },
        ];

        return [.. gates.Select(gate => gate with { Source = $"{gate.Source} ({source})" })];
    }

    /// <summary>
    /// PLAN section 13.3 decision path. No model is accepted because it is newer or larger,
    /// and this harness never names a 4B tag the source of truth does not define.
    /// </summary>
    public static ModelDecision Decide(string model, bool qualityPass, bool latencyPass) => (qualityPass, latencyPass) switch
    {
        (true, true) => new ModelDecision
        {
            Outcome = "quality-and-latency-pass",
            Recommendation = $"Recommend {model} for the Issue #9 demo-model freeze.",
        },
        (true, false) => new ModelDecision
        {
            Outcome = "quality-pass-latency-fail",
            Recommendation = $"Quality passes but warm latency fails; benchmark qwen3:1.7b next on the same dataset.",
        },
        (false, true) => new ModelDecision
        {
            Outcome = "quality-fail-latency-pass",
            Recommendation = $"{model} latency is acceptable but quality is insufficient; "
                + "PLAN allows an optional Qwen3.5 4B candidate, but no exact 4B tag is defined by the "
                + "source of truth, so confirm the tag before running it.",
        },
        (false, false) => new ModelDecision
        {
            Outcome = "quality-and-latency-fail",
            Recommendation = $"{model} fails both quality and latency; benchmark qwen3:1.7b next on the same dataset.",
        },
    };

    public static decimal Percent(int matched, int compared) =>
        compared == 0 ? 0 : decimal.Round(matched * 100m / compared, 1);

    public static string Format(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string FormatSeconds(double milliseconds) =>
        (milliseconds / 1000.0).ToString("F2", CultureInfo.InvariantCulture);
}
