using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// docs/TECHNICAL.md section 8.3 allows exactly one retry, and only for invalid structured
/// output. These tests pin that behaviour, including the fact that a semantic mismatch is
/// never retried.
/// </summary>
public sealed class NluAnalyzerTests
{
    private static readonly NluOutput Expected = BenchmarkFixtures.NoFilterSearch() with
    {
        Brand = "Dell",
        SizeInches = 24,
        RequiredPorts = ["HDMI"],
    };

    [Fact]
    public async Task A_schema_valid_reply_is_used_without_any_retry()
    {
        var gateway = new ScriptedGateway(
            """{"intent":"ProductSearch","brand":"Dell","sizeInches":24,"requiredPorts":["HDMI"],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.True(execution.SchemaValid);
        Assert.False(execution.Retried);
        Assert.Single(gateway.Requests);
        Assert.Null(execution.FailureReason);
        Assert.Equal("Dell", execution.Output!.Brand);
    }

    [Fact]
    public async Task An_invalid_reply_triggers_exactly_one_corrective_retry()
    {
        var gateway = new ScriptedGateway(
            """{"intent":"ProductSearch","brand":"Dell"}""",
            """{"intent":"ProductSearch","brand":"Dell","sizeInches":24,"requiredPorts":["HDMI"],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.True(execution.SchemaValid);
        Assert.True(execution.Retried);
        Assert.Equal(2, execution.Attempts.Length);
        Assert.False(execution.Attempts[0].Correction);
        Assert.True(execution.Attempts[1].Correction);
        Assert.Contains(
            "did not match the required JSON schema",
            LastMessageContent(gateway.Requests[1]),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_invalid_replies_end_as_a_malformed_case_with_a_reason()
    {
        var gateway = new ScriptedGateway("not json at all", """{"intent":"ProductSearch"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.False(execution.SchemaValid);
        Assert.True(execution.Retried);
        Assert.Null(execution.Output);
        Assert.Contains("schema-failure", execution.FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, gateway.Requests.Count);
    }

    [Fact]
    public async Task A_semantic_mismatch_is_never_retried()
    {
        var gateway = new ScriptedGateway(
            """{"intent":"ProductSearch","brand":"HP","requiredPorts":[],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.True(execution.SchemaValid);
        Assert.False(execution.Retried);
        Assert.Single(gateway.Requests);

        var comparison = NluOutputComparer.Compare(Expected, execution.Output);

        Assert.Contains(comparison.Mismatches, mismatch => mismatch.Field == "brand");
    }

    [Fact]
    public async Task A_transport_failure_is_recorded_and_retried_once()
    {
        var gateway = new ScriptedGateway(
            ScriptedGateway.TransportFailure,
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.True(execution.SchemaValid);
        Assert.Equal("scripted transport failure", execution.Attempts[0].TransportFailure);
        Assert.Equal(2, gateway.Requests.Count);
    }

    [Fact]
    public async Task Latency_is_recorded_per_attempt_and_for_the_whole_case()
    {
        var gateway = new ScriptedGateway("not json", """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.Equal(2, execution.Attempts.Length);
        Assert.Equal(execution.Attempts[^1].WallClockMilliseconds, execution.FinalAttemptMilliseconds);
        Assert.Equal(execution.Attempts.Sum(attempt => attempt.WallClockMilliseconds), execution.TotalMilliseconds);
    }

    private static string LastMessageContent(NluTransportRequest request)
    {
        var messages = request.Body["messages"]!.AsArray();

        return messages[^1]!["content"]!.GetValue<string>();
    }
}
