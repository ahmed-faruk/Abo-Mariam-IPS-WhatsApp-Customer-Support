using System.Text.Json;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>Checks Ollama model discovery without invoking inference.</summary>
public sealed class OllamaModelReadiness(
    IHttpClientFactory httpClientFactory,
    OllamaAiOptions options) : IAiModelReadiness
{
    public const string HttpClientName = "WhatsAppMonitorAssistant.Intelligence.OllamaTags";

    public async Task<AiModelReadinessStatus> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var response = await client.GetAsync("api/tags", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return AiModelReadinessStatus.Unavailable;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                return AiModelReadinessStatus.Unavailable;
            }

            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (Matches(model, "name", options.Model)
                    || Matches(model, "model", options.Model))
                {
                    return AiModelReadinessStatus.Ready;
                }
            }

            return AiModelReadinessStatus.ModelMissing;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return AiModelReadinessStatus.Unavailable;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return AiModelReadinessStatus.Unavailable;
        }
    }

    private static bool Matches(JsonElement model, string propertyName, string expected) =>
        model.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}
