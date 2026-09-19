using System.Text.Json.Nodes;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The documented retry policy of docs/TECHNICAL.md section 8.3, observed through the public
/// <see cref="IAiNluClient"/>: one corrective retry for a reply that arrived but failed validation,
/// no correction for a transport failure or a timeout, and caller cancellation left untouched.
/// </summary>
public sealed class OllamaAiNluClientTests
{
    private const string ValidReply =
        """
        {
          "intent": "ProductSearch",
          "brand": "Dell",
          "modelCode": null,
          "sizeInches": 24,
          "panel": null,
          "resolution": null,
          "minRefreshRate": null,
          "requiredPorts": ["HDMI"],
          "grades": [],
          "budgetType": "None",
          "budgetTarget": null,
          "budgetMin": null,
          "budgetMax": null,
          "useCase": null,
          "reference": null
        }
        """;

    private const string SemanticMismatchReply =
        """
        {
          "intent": "ProductSearch",
          "brand": null,
          "modelCode": null,
          "sizeInches": null,
          "panel": null,
          "resolution": null,
          "minRefreshRate": null,
          "requiredPorts": [],
          "grades": [],
          "budgetType": "Range",
          "budgetTarget": null,
          "budgetMin": 4000,
          "budgetMax": 2000,
          "useCase": null,
          "reference": null
        }
        """;

    [Fact]
    public async Task A_valid_first_reply_is_returned_without_a_retry()
    {
        var transport = new ScriptedTransport(OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.ProductSearch, result.Interpretation?.Intent);
        Assert.Equal("Dell", result.Interpretation?.Brand);
        Assert.False(result.RequiresClarification);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task A_malformed_first_reply_is_corrected_once_and_can_succeed()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.ReplyReceived("not json"),
            OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(2, transport.Attempts);
        Assert.Equal(3, transport.Requests[1]["messages"]?.AsArray().Count);
    }

    [Fact]
    public async Task A_schema_invalid_first_reply_is_corrected_once()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.ReplyReceived("""{"intent": "product_search", "requiredPorts": [], "grades": [], "budgetType": "None"}"""),
            OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(2, transport.Attempts);

        var correction = transport.Requests[1]["messages"]?.AsArray()[2]?["content"]?.GetValue<string>() ?? string.Empty;

        Assert.Contains("$.intent", correction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_semantically_invalid_first_reply_is_corrected_once()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.ReplyReceived(SemanticMismatchReply),
            OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "من 2000 لـ 4000",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task Two_invalid_replies_return_the_safe_clarification_result_without_a_third_request()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.ReplyReceived("not json"),
            OllamaChatTransportResult.ReplyReceived("still not json"));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.InvalidModelOutput, result.Status);
        Assert.True(result.RequiresClarification);
        Assert.Null(result.Interpretation);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task A_timeout_is_reported_without_a_schema_retry()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.TimedOut(),
            OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Timeout, result.Status);
        Assert.Null(result.Interpretation);
        Assert.False(result.RequiresClarification);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task An_unreachable_runtime_is_reported_without_a_schema_retry()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.Unavailable(),
            OllamaChatTransportResult.ReplyReceived(ValidReply));

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.AiUnavailable, result.Status);
        Assert.False(result.RequiresClarification);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task A_transport_failure_on_the_correction_attempt_is_reported_as_such()
    {
        var transport = new ScriptedTransport(
            OllamaChatTransportResult.ReplyReceived("not json"),
            OllamaChatTransportResult.TimedOut());

        var result = await CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            CancellationToken.None);

        Assert.Equal(NluAnalysisStatus.Timeout, result.Status);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_a_fallback()
    {
        var transport = new ScriptedTransport(OllamaChatTransportResult.ReplyReceived(ValidReply));
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            NluConversationContext.Empty,
            cancellation.Token));

        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task An_oversized_context_is_rejected_before_any_request()
    {
        var transport = new ScriptedTransport(OllamaChatTransportResult.ReplyReceived(ValidReply));
        var context = new NluConversationContext
        {
            PreviousCandidateLabels =
                [.. Enumerable.Range(0, NluConversationContext.MaxCandidateLabels + 1).Select(index => $"item {index}")],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => CreateClient(transport).AnalyzeAsync(
            "عندك ديل 24؟",
            context,
            CancellationToken.None));

        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task A_blank_message_is_rejected_before_any_request()
    {
        var transport = new ScriptedTransport(OllamaChatTransportResult.ReplyReceived(ValidReply));

        await Assert.ThrowsAsync<ArgumentException>(() => CreateClient(transport).AnalyzeAsync(
            "   ",
            NluConversationContext.Empty,
            CancellationToken.None));

        Assert.Equal(0, transport.Attempts);
    }

    private static OllamaAiNluClient CreateClient(IOllamaChatTransport transport) => new(
        transport,
        new OllamaChatRequestBuilder(
            new OllamaAiOptions
            {
                Provider = OllamaFrozenProfile.Provider,
                BaseUrl = OllamaFrozenProfile.BaseUrl,
                Model = OllamaFrozenProfile.Model,
                TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds,
                Temperature = OllamaFrozenProfile.Temperature,
                ContextTokens = OllamaFrozenProfile.ContextTokens,
            },
            NluOutputSchema.Load()));

    private sealed class ScriptedTransport(params OllamaChatTransportResult[] results) : IOllamaChatTransport
    {
        private readonly Queue<OllamaChatTransportResult> _results = new(results);

        public List<JsonObject> Requests { get; } = [];

        public int Attempts => Requests.Count;

        public Task<OllamaChatTransportResult> SendAsync(
            JsonObject request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            return Task.FromResult(_results.Dequeue());
        }
    }
}
