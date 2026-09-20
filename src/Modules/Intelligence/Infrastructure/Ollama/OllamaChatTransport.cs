using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The HTTP half of the adapter: one <c>POST /api/chat</c> and the envelope around it. The
/// <see cref="HttpClient"/> is registered with an infinite timeout so this class can tell the two
/// cancellations apart — the caller's token is rethrown, while the configured AI timeout becomes an
/// outcome the caller maps to the safe fallback of docs/TECHNICAL.md section 30. A structured-NLU
/// envelope is tiny, so the response is read through a fixed size cap: a runtime that streams megabytes
/// must not become an allocation here, and an oversized body is an unusable envelope rather than a
/// model reply that could be corrected.
/// </summary>
public sealed class OllamaChatTransport : IOllamaChatTransport
{
    /// <summary>The documented chat endpoint (docs/TECHNICAL.md section 8.2).</summary>
    public const string ChatPath = "api/chat";

    /// <summary>
    /// The largest Ollama envelope this adapter reads, in bytes. The frozen schema returns a short JSON
    /// object, so 64 KiB is far above any valid reply while staying far below an unbounded buffer.
    /// </summary>
    public const int MaxResponseBytes = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly ILogger<OllamaChatTransport> _logger;

    public OllamaChatTransport(
        HttpClient httpClient,
        OllamaAiOptions options,
        ILogger<OllamaChatTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _logger = logger;
    }

    public async Task<OllamaChatTransportResult> SendAsync(
        JsonObject request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        HttpResponseMessage response;

        try
        {
            using var body = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
            response = await _httpClient.PostAsync(ChatPath, body, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "The Ollama chat request to /{Endpoint} exceeded the configured AI timeout before a "
                + "response arrived.",
                ChatPath);

            return OllamaChatTransportResult.TimedOut();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "The Ollama chat request to /{Endpoint} failed before a response arrived.",
                ChatPath);

            return OllamaChatTransportResult.Unavailable();
        }

        using (response)
        {
            string payload;

            try
            {
                payload = await ReadResponseAsync(response, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Reading the Ollama chat response from /{Endpoint} exceeded the configured AI timeout.",
                    ChatPath);

                return OllamaChatTransportResult.TimedOut();
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Reading the Ollama chat response from /{Endpoint} failed.",
                    ChatPath);

                return OllamaChatTransportResult.Unavailable();
            }
            catch (InvalidDataException exception)
            {
                _logger.LogWarning(
                    exception,
                    "The Ollama chat response from /{Endpoint} was unusable ({FailureKind}).",
                    ChatPath,
                    exception.GetType().Name);

                return OllamaChatTransportResult.Unavailable();
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "The Ollama chat request to /{Endpoint} returned status {StatusCode}.",
                    ChatPath,
                    (int)response.StatusCode);

                return OllamaChatTransportResult.Unavailable();
            }

            // A reply that arrives without a usable message.content is an unusable envelope, not a
            // model answer, so it is never corrected and retried.
            if (TryReadContent(payload, out var content))
            {
                return OllamaChatTransportResult.ReplyReceived(content);
            }

            _logger.LogWarning(
                "The Ollama chat response from /{Endpoint} was not a usable chat envelope.",
                ChatPath);

            return OllamaChatTransportResult.Unavailable();
        }
    }

    /// <summary>
    /// Reads the response as UTF-8 while enforcing <see cref="MaxResponseBytes"/>, so an oversized body
    /// is detected during the read and never buffered in full.
    /// </summary>
    private static async Task<string> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stream.CanSeek && stream.Length > MaxResponseBytes)
        {
            throw new InvalidDataException("The Ollama response exceeded the maximum envelope size.");
        }

        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = ArrayPool<byte>.Shared.Rent(4096);
        var characters = ArrayPool<char>.Shared.Rent(4096);

        try
        {
            var payload = new StringBuilder();
            var readTotal = 0;

            while (true)
            {
                var read = await stream
                    .ReadAsync(bytes.AsMemory(0, 4096), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                readTotal += read;

                if (readTotal > MaxResponseBytes)
                {
                    throw new InvalidDataException("The Ollama response exceeded the maximum envelope size.");
                }

                var decoded = decoder.GetChars(bytes, 0, read, characters, 0, flush: false);

                payload.Append(characters, 0, decoded);
            }

            var final = decoder.GetChars(bytes, 0, 0, characters, 0, flush: true);

            payload.Append(characters, 0, final);

            return payload.ToString();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(characters);
        }
    }

    private static bool TryReadContent(string payload, out string content)
    {
        content = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var node)
                || node.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            content = node.GetString() ?? string.Empty;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
