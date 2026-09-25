using Microsoft.Extensions.Logging;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The Demo-Critical Gate v1 section D timing evidence of docs/TECHNICAL.md section 36.6: exactly one
/// Information event per interpreted turn, carrying only the provider message id, the NLU status and the
/// elapsed milliseconds measured with the application's clock.
/// </summary>
public sealed class NluTimingLogTests
{
    private const string Body = "عندك ديل 24؟";
    private const string CustomerId = "20100000001";
    private const string ProviderMessageId = "wamid.turn-1";

    [Fact]
    public async Task L1_an_interpreted_text_turn_logs_one_timing_event_with_only_id_status_and_duration()
    {
        var (handler, logger) = Build(_ => NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await handler.ProcessAsync(ConversationSamples.Text(Body, ProviderMessageId, CustomerId));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            "NLU analysis for inbound wamid.turn-1 finished with Success in 1234 ms",
            entry.Message);
        Assert.Equal(
            ["ProviderMessageId=wamid.turn-1", "NluStatus=Success", "ElapsedMilliseconds=1234"],
            entry.Properties);
        Assert.DoesNotContain(Body, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerId, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("image", Body, ConversationModes.Ai)]
    [InlineData("text", "   ", ConversationModes.Ai)]
    [InlineData("text", Body, ConversationModes.Human)]
    public async Task L2_a_turn_that_is_not_interpreted_logs_no_timing_event(string messageType, string body, string mode)
    {
        var (handler, logger) = Build(
            _ => NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)),
            mode);

        await handler.ProcessAsync(ConversationSamples.Text(body, ProviderMessageId, CustomerId, messageType: messageType));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task L3_an_unavailable_model_is_logged_with_its_status()
    {
        var (handler, logger) = Build(_ => NluAnalysisResult.AiUnavailable());

        await handler.ProcessAsync(ConversationSamples.Text(Body, ProviderMessageId, CustomerId));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            ["ProviderMessageId=wamid.turn-1", "NluStatus=AiUnavailable", "ElapsedMilliseconds=1234"],
            entry.Properties);
    }

    [Fact]
    public async Task L4_an_analysis_that_throws_logs_no_timing_event()
    {
        var (handler, logger) = Build(_ => throw new InvalidOperationException("the model call failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ProcessAsync(ConversationSamples.Text(Body, ProviderMessageId, CustomerId)));

        Assert.Empty(logger.Entries);
    }

    private static (ProcessInboundTurnHandler Handler, CapturingLogger Logger) Build(
        Func<string, NluAnalysisResult> answer,
        string mode = ConversationModes.Ai)
    {
        var store = new FakeConversationTurnStore { Mode = mode };
        var nlu = new FakeAiNluClient { Answer = answer };
        var router = new ConversationIntentRouter(new FakeCatalogSearch(), new FakeCatalogProductDetails());
        var logger = new CapturingLogger();

        var handler = new ProcessInboundTurnHandler(
            store,
            router,
            nlu,
            new FakeConversationRenderer(),
            new RecordingOutboundMessageQueue(),
            new SteppingClock(ConversationSamples.Now, TimeSpan.FromMilliseconds(1234)),
            logger);

        return (handler, logger);
    }

    /// <summary>A clock whose timestamp advances by a fixed step on every read, so a duration is exact.</summary>
    private sealed class SteppingClock(DateTime utcNow, TimeSpan step) : TimeProvider
    {
        private readonly DateTimeOffset now = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeSpan.Zero);
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => now;

        public override long GetTimestamp() => timestamp += step.Ticks;
    }

    private sealed class CapturingLogger : ILogger<ProcessInboundTurnHandler>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> values
                ? values
                    .Where(pair => pair.Key != "{OriginalFormat}")
                    .Select(pair => $"{pair.Key}={pair.Value}")
                    .ToList()
                : [];

            Entries.Add(new Entry(logLevel, formatter(state, exception), properties));
        }

        public sealed record Entry(LogLevel Level, string Message, IReadOnlyList<string> Properties);
    }
}
