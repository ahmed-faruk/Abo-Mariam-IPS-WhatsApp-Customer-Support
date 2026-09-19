using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The HTTP half of the adapter: one <c>POST /api/chat</c> and the envelope around it. The
/// <see cref="HttpClient"/> is registered with an infinite timeout so this class can tell the two
/// cancellations apart — the caller's token is rethrown, while the configured AI timeout becomes an
/// outcome the caller maps to the safe fallback of docs/TECHNICAL.md section 30.
/// </summary>
public sealed class OllamaChatTransport : IOllamaChatTransport
{
    /// <summary>The documented chat endpoint (docs/TECHNICAL.md section 8.2).</summary>
    public const string ChatPath = "api/chat";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    public OllamaChatTransport(HttpClient httpClient, OllamaAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
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
            return OllamaChatTransportResult.TimedOut();
        }
        catch (HttpRequestException)
        {
            return OllamaChatTransportResult.Unavailable();
        }

        using (response)
        {
            string payload;

            try
            {
                payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return OllamaChatTransportResult.TimedOut();
            }
            catch (HttpRequestException)
            {
                return OllamaChatTransportResult.Unavailable();
            }

            if (!response.IsSuccessStatusCode)
            {
                return OllamaChatTransportResult.Unavailable();
            }

            // A reply that arrives without a usable message.content is an unusable envelope, not a
            // model answer, so it is never corrected and retried.
            return TryReadContent(payload, out var content)
                ? OllamaChatTransportResult.ReplyReceived(content)
                : OllamaChatTransportResult.Unavailable();
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
