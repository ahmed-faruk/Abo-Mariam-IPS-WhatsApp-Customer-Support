using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// The gates are the deliverable, so they are pinned to docs/PLAN.md section 13.3 values and
/// to the exact rules that feed them.
/// </summary>
public sealed class BenchmarkEvaluationTests
{
    private static readonly NluOutput SuccessfulReply = BenchmarkJson.Deserialize<NluOutput>(
        """{"intent":"ProductSearch","brand":"Dell","sizeInches":24,"panel":"IPS","requiredPorts":["HDMI"],"grades":[],"budgetType":"None"}""");

    [Fact]
    public void Gates_use_the_plan_thresholds_and_ignore_observational_cases()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, milliseconds: 1000),
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001"), milliseconds: 2000),
            BenchmarkFixtures.Execution("AMB-001", BenchmarkFixtures.Expected("AMB-001"), milliseconds: 3000));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.Equal(2, evaluation.Metrics.CaseCount);
        Assert.Equal(1, evaluation.Metrics.ObservationalCaseCount);
        Assert.Equal(100m, evaluation.Metrics.IntentAccuracyPercent);
        Assert.Equal(100m, evaluation.Metrics.HardBudgetAccuracyPercent);
        Assert.Equal(100m, evaluation.Metrics.SchemaValidPercent);
        Assert.True(evaluation.OverallPass);
    }

    [Fact]
    public void Intent_accuracy_is_computed_over_gating_cases_only()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply),
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001")),
            BenchmarkFixtures.Execution("GREET-001", SuccessfulReply));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.Equal(3, evaluation.Metrics.CaseCount);
        Assert.Equal(2, evaluation.Metrics.IntentCorrectCount);
        Assert.Equal(66.7m, evaluation.Metrics.IntentAccuracyPercent);
        Assert.False(Gate(evaluation, "Intent accuracy"));
    }

    [Fact]
    public void Hard_budget_gate_requires_one_hundred_percent_of_the_dedicated_cases()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001")),
            BenchmarkFixtures.Execution("BUD-HARD-002", BenchmarkFixtures.Expected("BUD-HARD-002")));

        var passing = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.Equal(2, passing.Metrics.HardBudgetCaseCount);
        Assert.Equal(100m, passing.Metrics.HardBudgetAccuracyPercent);
        Assert.True(Gate(passing, "Hard-budget classification"));

        var wrongCeiling = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001")),
            BenchmarkFixtures.Execution(
                "BUD-HARD-002",
                BenchmarkFixtures.Expected("BUD-HARD-002") with { BudgetTarget = 3000m }));

        var failing = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [wrongCeiling]);

        Assert.Equal(50m, failing.Metrics.HardBudgetAccuracyPercent);
        Assert.False(Gate(failing, "Hard-budget classification"));
    }

    [Fact]
    public void A_soft_classification_never_counts_as_a_correct_hard_budget()
    {
        var expected = BenchmarkFixtures.Expected("BUD-HARD-001");
        var actual = expected with { BudgetType = "Soft" };

        Assert.True(BenchmarkEvaluator.IsHardBudgetCorrect(expected, expected));
        Assert.False(BenchmarkEvaluator.IsHardBudgetCorrect(expected, actual));
        Assert.False(BenchmarkEvaluator.IsHardBudgetCorrect(expected, null));
    }

    [Fact]
    public void A_hard_ceiling_carried_in_budgetMax_is_accepted_by_the_resolution_rule()
    {
        var expected = BenchmarkFixtures.Expected("BUD-HARD-001");
        var actual = expected with { BudgetTarget = null, BudgetMax = expected.BudgetTarget };

        Assert.True(BenchmarkEvaluator.IsHardBudgetCorrect(expected, actual));
    }

    [Fact]
    public void Schema_gate_counts_only_replies_that_became_valid_after_at_most_one_retry()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, retried: true),
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001"), milliseconds: 900),
            BenchmarkFixtures.Execution(
                "GREET-001",
                output: null,
                schemaValid: false,
                retried: true,
                failureReason: "schema-failure: $.grades is missing"));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.Equal(2, evaluation.Metrics.SchemaValidCount);
        Assert.Equal(66.7m, evaluation.Metrics.SchemaValidPercent);
        Assert.Equal(1, evaluation.Metrics.MalformedCount);
        Assert.Equal(2, evaluation.Metrics.RetryCount);
        Assert.False(Gate(evaluation, "Structured schema success after one retry"));
    }

    [Fact]
    public void Warm_latency_uses_the_scored_attempt_and_the_documented_percentiles()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, milliseconds: 1000),
            BenchmarkFixtures.Execution("PS-002", SuccessfulReply, milliseconds: 2000),
            BenchmarkFixtures.Execution("PS-003", SuccessfulReply, milliseconds: 3000));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.Equal(2000, evaluation.Metrics.WarmMedianMilliseconds);
        Assert.Equal(2900, evaluation.Metrics.WarmP95Milliseconds, 6);
        Assert.True(Gate(evaluation, "Warm median latency"));
        Assert.True(Gate(evaluation, "Warm p95 latency"));
    }

    [Fact]
    public void A_slow_run_fails_only_the_latency_gates()
    {
        var run = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, milliseconds: 30_000),
            BenchmarkFixtures.Execution("PS-002", SuccessfulReply, milliseconds: 40_000),
            BenchmarkFixtures.Execution("BUD-HARD-001", BenchmarkFixtures.Expected("BUD-HARD-001"), milliseconds: 50_000));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run]);

        Assert.True(Gate(evaluation, "Intent accuracy"));
        Assert.True(Gate(evaluation, "Hard-budget classification"));
        Assert.True(Gate(evaluation, "Structured schema success after one retry"));
        Assert.False(Gate(evaluation, "Warm median latency"));
        Assert.False(Gate(evaluation, "Warm p95 latency"));
        Assert.False(evaluation.OverallPass);
        Assert.Equal("quality-pass-latency-fail", evaluation.Decision.Outcome);
    }

    [Fact]
    public void Two_runs_are_combined_before_the_percentiles_are_computed()
    {
        var run1 = BenchmarkFixtures.Run(
            "run1",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, milliseconds: 1000),
            BenchmarkFixtures.Execution("PS-002", SuccessfulReply, milliseconds: 3000));
        var run2 = BenchmarkFixtures.Run(
            "run2",
            BenchmarkFixtures.Execution("PS-001", SuccessfulReply, milliseconds: 2000),
            BenchmarkFixtures.Execution("PS-002", SuccessfulReply, milliseconds: 4000));

        var evaluation = BenchmarkEvaluator.Evaluate(BenchmarkFixtures.Manifest, BenchmarkFixtures.Dataset, [run1, run2]);

        Assert.Equal(4, evaluation.Metrics.CaseCount);
        Assert.Equal(2500, evaluation.Metrics.WarmMedianMilliseconds);
        Assert.Equal(3850, evaluation.Metrics.WarmP95Milliseconds, 6);
    }

    [Theory]
    [InlineData(true, true, "quality-and-latency-pass", "Issue #9")]
    [InlineData(true, false, "quality-pass-latency-fail", "qwen3:1.7b")]
    [InlineData(false, true, "quality-fail-latency-pass", "no exact 4B tag is defined")]
    [InlineData(false, false, "quality-and-latency-fail", "qwen3:1.7b")]
    public void Decision_follows_the_plan_branch_it_measured(
        bool qualityPass,
        bool latencyPass,
        string outcome,
        string expectedText)
    {
        var decision = BenchmarkEvaluator.Decide("qwen3.5:2b-q4_K_M", qualityPass, latencyPass);

        Assert.Equal(outcome, decision.Outcome);
        Assert.Contains(expectedText, decision.Recommendation, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_gate_carries_its_source_and_threshold()
    {
        var evaluation = BenchmarkEvaluator.Evaluate(
            BenchmarkFixtures.Manifest,
            BenchmarkFixtures.Dataset,
            [BenchmarkFixtures.Run("run1", BenchmarkFixtures.Execution("PS-001", SuccessfulReply))]);

        Assert.Equal(5, evaluation.Gates.Length);
        Assert.All(evaluation.Gates, gate => Assert.Contains("docs/PLAN.md section 13.3", gate.Source, StringComparison.Ordinal));
        Assert.Contains(evaluation.Gates, gate => gate.Name == "Hard-budget classification" && gate.Threshold.Contains("100", StringComparison.Ordinal));
        Assert.Contains(evaluation.Gates, gate => gate.Name == "Warm median latency" && gate.Threshold.Contains("8 s", StringComparison.Ordinal));
    }

    private static bool Gate(BenchmarkEvaluation evaluation, string name) =>
        evaluation.Gates.Single(gate => gate.Name == name).Passed;
}
