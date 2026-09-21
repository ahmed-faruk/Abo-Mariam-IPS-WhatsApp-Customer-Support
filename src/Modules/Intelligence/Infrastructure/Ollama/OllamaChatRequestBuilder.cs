using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// Builds the Ollama <c>/api/chat</c> body of docs/TECHNICAL.md section 8.2: the frozen system prompt,
/// the customer message, schema-constrained JSON output, <c>stream: false</c>, <c>think: false</c> and
/// the configured temperature and context target. None of those switches is configuration: they are
/// the measured request shape, and a change is a new benchmark rather than a setting.
/// </summary>
public sealed class OllamaChatRequestBuilder
{
    private readonly OllamaAiOptions _options;
    private readonly NluOutputSchema _schema;

    public OllamaChatRequestBuilder(OllamaAiOptions options, NluOutputSchema schema)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schema);

        _options = options;
        _schema = schema;
    }

    /// <summary>
    /// Builds one request. <paramref name="correctionProblems"/> is the single corrective follow-up of
    /// the documented retry policy; it is application-generated and never customer text.
    /// </summary>
    public JsonObject Build(string message, IReadOnlyList<string>? correctionProblems = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = NluSystemPrompt.Text,
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = message,
            },
        };

        if (correctionProblems is { Count: > 0 })
        {
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = NluSystemPrompt.BuildCorrection(correctionProblems),
            });
        }

        return new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["think"] = false,
            ["format"] = _schema.Format.DeepClone(),
            ["options"] = new JsonObject
            {
                ["temperature"] = _options.Temperature,
                ["num_ctx"] = _options.ContextTokens,
            },
        };
    }
}
