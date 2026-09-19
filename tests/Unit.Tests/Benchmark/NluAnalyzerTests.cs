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
    public async Task A_transport_failure_never_triggers_a_schema_corrective_retry()
    {
        var gateway = new ScriptedGateway(
            ScriptedGateway.TransportFailure,
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.False(execution.SchemaValid);
        Assert.False(execution.Retried);
        Assert.Single(execution.Attempts);
        Assert.Equal("scripted transport failure", execution.Attempts[0].TransportFailure);
        Assert.Contains("transport-failure", execution.FailureReason, StringComparison.Ordinal);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task A_transport_failure_cannot_disappear_behind_a_later_valid_response()
    {
        var gateway = new ScriptedGateway(
            ScriptedGateway.TransportFailure,
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""");
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        // The scripted valid reply is still queued: the outage stays visible as infrastructure
        // evidence instead of being scored as a successful case.
        Assert.Single(gateway.Requests);
        Assert.False(execution.SchemaValid);
        Assert.Null(execution.Output);
    }

    [Fact]
    public async Task Latency_comes_from_the_independently_measured_attempt_durations()
    {
        // Durations are authored by the test and injected through the timing seam, so the
        // assertion does not restate the production getters it is meant to check.
        var clock = new ManualTimeProvider();
        var gateway = new TimedGateway(
            clock,
            ("not json at all", 1500),
            ("""{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""", 250));
        var analyzer = BenchmarkFixtures.CreateAnalyzer(gateway, timeProvider: clock);

        var execution = await analyzer.AnalyzeAsync(
            BenchmarkFixtures.CreateCase("PS-001", Expected),
            CancellationToken.None);

        Assert.Equal(2, execution.Attempts.Length);
        Assert.Equal(1500, execution.Attempts[0].WallClockMilliseconds);
        Assert.Equal(250, execution.Attempts[1].WallClockMilliseconds);
        Assert.Equal(250, execution.FinalAttemptMilliseconds);
        Assert.Equal(1750, execution.TotalMilliseconds);
    }

    private static string LastMessageContent(NluTransportRequest request)
    {
        var messages = request.Body["messages"]!.AsArray();

        return messages[^1]!["content"]!.GetValue<string>();
    }

    /// <summary>A monotonic clock the tests advance by hand, in milliseconds.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(long milliseconds) => _timestamp += milliseconds * TimeSpan.TicksPerMillisecond;
    }

    /// <summary>Returns each scripted reply after advancing the fake clock by its authored duration.</summary>
    private sealed class TimedGateway : INluTransport
    {
        private readonly ManualTimeProvider _clock;
        private readonly Queue<(string Content, long Milliseconds)> _script;

        public TimedGateway(ManualTimeProvider clock, params (string Content, long Milliseconds)[] script)
        {
            _clock = clock;
            _script = new Queue<(string Content, long Milliseconds)>(script);
        }

        public Task<NluTransportResponse> SendAsync(
            NluTransportRequest request,
            CancellationToken cancellationToken)
        {
            var next = _script.Dequeue();
            _clock.Advance(next.Milliseconds);

            return Task.FromResult(new NluTransportResponse { Content = next.Content });
        }
    }
}
