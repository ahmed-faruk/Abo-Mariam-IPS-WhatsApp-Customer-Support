using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>Raised when Ollama cannot be reached, does not answer in time, or returns an error.</summary>
public sealed class OllamaTransportException : Exception
{
    public OllamaTransportException(string message)
        : base(message)
    {
    }

    public OllamaTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record OllamaOptions
{
    public required string BaseUrl { get; init; }

    public required int TimeoutSeconds { get; init; }
}

/// <summary>What the harness verifies before it measures anything.</summary>
public sealed record OllamaHealth(string? Version, IReadOnlyList<string> Models);

/// <summary>
/// The live Ollama side of the harness: a health/model check before measuring, plus the
/// chat transport. Implemented by the real client and by the offline fixture.
/// </summary>
public interface IOllamaGateway : INluTransport
{
    Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Minimal Ollama client: health/model discovery plus <c>/api/chat</c>. Nothing else is
/// implemented because the benchmark must not grow into a production AI adapter.
/// </summary>
public sealed class OllamaNluClient : IOllamaGateway
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaNluClient(HttpClient httpClient, OllamaOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
    {
        var payload = await GetJsonAsync("api/version", cancellationToken).ConfigureAwait(false);
        return payload.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var payload = await GetJsonAsync("api/tags", cancellationToken).ConfigureAwait(false);

        if (!payload.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. models.EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!),
        ];
    }

    public async Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
        new(await GetVersionAsync(cancellationToken).ConfigureAwait(false),
            await ListModelsAsync(cancellationToken).ConfigureAwait(false));

    public async Task<NluTransportResponse> SendAsync(
        NluTransportRequest request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient
                .PostAsJsonAsync("api/chat", request.Body, BenchmarkJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaTransportException(
                $"Ollama did not answer within {_options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaTransportException($"Ollama is not reachable at {_options.BaseUrl}.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new OllamaTransportException(
                    $"Ollama returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}");
            }

            return ParseChatResponse(body);
        }
    }

    private async Task<JsonElement> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaTransportException(
                $"Ollama did not answer {relativeUrl} within {_options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaTransportException($"Ollama is not reachable at {_options.BaseUrl}.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new OllamaTransportException($"Ollama did not expose {relativeUrl}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new OllamaTransportException(
                    $"Ollama returned {(int)response.StatusCode} for {relativeUrl}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new OllamaTransportException($"Ollama returned invalid JSON for {relativeUrl}.", exception);
            }
        }
    }

    private static NluTransportResponse ParseChatResponse(string body)
    {
        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new OllamaTransportException("Ollama returned a body that is not valid JSON.", exception);
        }

        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw new OllamaTransportException("Ollama response did not contain message.content.");
        }

        return new NluTransportResponse
        {
            Content = content.GetString() ?? string.Empty,
            Timing = ReadTiming(root),
        };
    }

    private static NluTransportTiming? ReadTiming(JsonElement root)
    {
        var timing = new NluTransportTiming
        {
            TotalDurationNanoseconds = ReadLong(root, "total_duration"),
            LoadDurationNanoseconds = ReadLong(root, "load_duration"),
            PromptEvalCount = ReadInt(root, "prompt_eval_count"),
            PromptEvalDurationNanoseconds = ReadLong(root, "prompt_eval_duration"),
            EvalCount = ReadInt(root, "eval_count"),
            EvalDurationNanoseconds = ReadLong(root, "eval_duration"),
        };

        return timing == new NluTransportTiming() ? null : timing;
    }

    private static long? ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static int? ReadInt(JsonElement root, string name) =>
        ReadLong(root, name) is { } value && value <= int.MaxValue ? (int)value : null;

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : string.Concat(value.AsSpan(0, 400), "...");

    internal static string FormatNanoseconds(long nanoseconds) =>
        (nanoseconds / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture);
}
