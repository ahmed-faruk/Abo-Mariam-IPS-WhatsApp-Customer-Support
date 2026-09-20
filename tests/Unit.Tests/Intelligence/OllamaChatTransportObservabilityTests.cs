using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// A failed Ollama call is diagnosed from the log, so the transport records only safe metadata — the
/// endpoint, the HTTP status and the exception object. The customer message, the frozen prompt and the
/// model's own content never reach a log entry.
/// </summary>
public sealed class OllamaChatTransportObservabilityTests
{
    private const string CustomerMessage = "عندك ديل 24 وميزانيتي 3000؟";
    private const string ModelContent = """{"intent": "ProductSearch", "price": 3999.56}""";

    [Fact]
    public async Task A_connection_failure_keeps_the_safe_outcome_and_logs_the_endpoint()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("connection refused")));
        var transport = CreateTransport(logger, handler);

        var result = await transport.SendAsync(EmptyRequest(CustomerMessage), CancellationToken.None);

        Assert.Equal(OllamaChatOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Content);
        Assert.Contains(logger.Entries, entry => entry.Contains("api/chat", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
        AssertSafetyInvariants(logger);
    }

    [Fact]
    public async Task A_non_success_status_keeps_the_safe_outcome_and_logs_the_status()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream error text", Encoding.UTF8, "application/json"),
        }));
        var transport = CreateTransport(logger, handler);

        var result = await transport.SendAsync(EmptyRequest(CustomerMessage), CancellationToken.None);

        Assert.Equal(OllamaChatOutcome.Unavailable, result.Outcome);
        Assert.Contains(logger.Entries, entry => entry.Contains("502", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("upstream error text", StringComparison.Ordinal));
        AssertSafetyInvariants(logger);
    }

    [Fact]
    public async Task A_usable_reply_is_returned_and_its_content_is_never_logged()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FakeEnvelope(ModelContent), Encoding.UTF8, "application/json"),
        }));
        var transport = CreateTransport(logger, handler);

        var result = await transport.SendAsync(EmptyRequest(CustomerMessage), CancellationToken.None);

        Assert.Equal(OllamaChatOutcome.ReplyReceived, result.Outcome);
        Assert.Equal(ModelContent, result.Content);
        AssertSafetyInvariants(logger);
    }

    [Fact]
    public async Task An_unusable_envelope_keeps_the_safe_outcome_and_logs_no_payload()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message": {"role": "assistant"}}""", Encoding.UTF8, "application/json"),
        }));
        var transport = CreateTransport(logger, handler);

        var result = await transport.SendAsync(EmptyRequest(CustomerMessage), CancellationToken.None);

        Assert.Equal(OllamaChatOutcome.Unavailable, result.Outcome);
        Assert.Contains(logger.Entries, entry => entry.Contains("envelope", StringComparison.Ordinal));
        AssertSafetyInvariants(logger);
    }

    private static void AssertSafetyInvariants(CapturingLogger logger)
    {
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(CustomerMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(ModelContent, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("3999.56", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain('\n', entry));
    }

    private static JsonObject EmptyRequest(string customerMessage) => new()
    {
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = NluSystemPrompt.Text },
            new JsonObject { ["role"] = "user", ["content"] = customerMessage },
        },
    };

    private static string FakeEnvelope(string content) => new JsonObject
    {
        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
        ["done"] = true,
    }.ToJsonString();

    private static OllamaChatTransport CreateTransport(CapturingLogger logger, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(OllamaFrozenProfile.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return new OllamaChatTransport(
            httpClient,
            new OllamaAiOptions
            {
                Provider = OllamaFrozenProfile.Provider,
                BaseUrl = OllamaFrozenProfile.BaseUrl,
                Model = OllamaFrozenProfile.Model,
                TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds,
                Temperature = OllamaFrozenProfile.Temperature,
                ContextTokens = OllamaFrozenProfile.ContextTokens,
            },
            logger);
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }

    /// <summary>Records what an operator would see, including the exception a structured log carries.</summary>
    private sealed class CapturingLogger : ILogger<OllamaChatTransport>
    {
        public List<string> Entries { get; } = [];

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
            ArgumentNullException.ThrowIfNull(formatter);

            Entries.Add(exception is null
                ? formatter(state, exception)
                : $"{exception.GetType().Name}: {formatter(state, exception)}");
        }
    }
}
