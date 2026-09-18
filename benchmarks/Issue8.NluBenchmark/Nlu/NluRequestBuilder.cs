using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Builds the Ollama <c>/api/chat</c> body documented in docs/TECHNICAL.md sections 8.2 and
/// 8.3: schema-constrained JSON output, <c>stream: false</c>, the configured temperature and
/// the configured context target.
/// </summary>
public sealed class NluRequestBuilder
{
    private readonly JsonElement _schema;

    public NluRequestBuilder(JsonElement schema)
    {
        _schema = schema;
    }

    public static NluRequestBuilder FromFile(string schemaPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return new NluRequestBuilder(document.RootElement.Clone());
    }

    public JsonObject Build(NluRequestParameters parameters, string userInput, IReadOnlyList<string>? schemaErrors = null)
    {
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
                ["content"] = userInput,
            },
        };

        if (schemaErrors is { Count: > 0 })
        {
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = NluSystemPrompt.BuildCorrection(schemaErrors),
            });
        }

        return new JsonObject
        {
            ["model"] = parameters.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["format"] = JsonNode.Parse(_schema.GetRawText()),
            ["options"] = new JsonObject
            {
                ["temperature"] = parameters.Temperature,
                ["num_ctx"] = parameters.ContextTokens,
            },
        };
    }
}
