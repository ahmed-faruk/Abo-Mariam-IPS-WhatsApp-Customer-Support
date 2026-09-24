using System.Globalization;
using System.Text.Json.Nodes;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// One scripted Ollama harness for the Intelligence seam tests: the production
/// <see cref="OllamaAiNluClient"/> wired to a scripted <see cref="IOllamaChatTransport"/>, plus a
/// builder for a schema-valid model reply. It exists so every Intelligence behaviour test drives the
/// same public seam and the same request builder as production, without duplicating the transport.
/// </summary>
internal static class ScriptedOllamaClient
{
    /// <summary>The production client over the frozen demo profile, with the transport replaced.</summary>
    public static OllamaAiNluClient CreateClient(IOllamaChatTransport transport) => new(
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

    /// <summary>A transport that answers with the given results, in order, and records the requests.</summary>
    public static ScriptedTransport Transport(params OllamaChatTransportResult[] results) => new(results);

    /// <summary>
    /// A schema-valid model reply carrying exactly the fifteen contract fields. Every optional field
    /// defaults to null and every collection field to empty, so a test states only what it asserts.
    /// </summary>
    public static string Reply(
        string intent,
        string? brand = null,
        string? modelCode = null,
        decimal? sizeInches = null,
        string? panel = null,
        string? resolution = null,
        int? minRefreshRate = null,
        IReadOnlyList<string>? requiredPorts = null,
        IReadOnlyList<string>? grades = null,
        string budgetType = "None",
        decimal? budgetTarget = null,
        decimal? budgetMin = null,
        decimal? budgetMax = null,
        string? useCase = null,
        string? reference = null)
    {
        var reply = new JsonObject
        {
            ["intent"] = intent,
            ["brand"] = brand,
            ["modelCode"] = modelCode,
            ["sizeInches"] = sizeInches,
            ["panel"] = panel,
            ["resolution"] = resolution,
            ["minRefreshRate"] = minRefreshRate,
            ["requiredPorts"] = new JsonArray([.. (requiredPorts ?? []).Select(value => (JsonNode)value)]),
            ["grades"] = new JsonArray([.. (grades ?? []).Select(value => (JsonNode)value)]),
            ["budgetType"] = budgetType,
            ["budgetTarget"] = budgetTarget,
            ["budgetMin"] = budgetMin,
            ["budgetMax"] = budgetMax,
            ["useCase"] = useCase,
            ["reference"] = reference,
        };

        return reply.ToJsonString();
    }

    /// <summary>A scripted transport that answers in order and records every request it received.</summary>
    internal sealed class ScriptedTransport(params OllamaChatTransportResult[] results) : IOllamaChatTransport
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
