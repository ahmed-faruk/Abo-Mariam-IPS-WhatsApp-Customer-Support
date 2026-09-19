using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The structured-output JSON schema the adapter sends as Ollama's <c>format</c>. The embedded bytes
/// are the frozen schema of docs/TECHNICAL.md section 8.3 — the same document the Issue #8 benchmark
/// measured — so production never reads the schema from the benchmark project.
/// </summary>
public sealed class NluOutputSchema
{
    /// <summary>The logical name of the embedded schema resource.</summary>
    public const string ResourceName = "WhatsAppMonitorAssistant.Modules.Intelligence.nlu-output.schema.json";

    private NluOutputSchema(string text, string sha256)
    {
        Text = text;
        Sha256 = sha256;
        Format = JsonNode.Parse(text)
            ?? throw new InvalidOperationException("The structured-output schema is not a JSON document.");
    }

    /// <summary>The schema text exactly as it is embedded.</summary>
    public string Text { get; }

    /// <summary>Lowercase SHA-256 of the embedded schema bytes.</summary>
    public string Sha256 { get; }

    /// <summary>
    /// The schema as a JSON node. Callers clone it before attaching it to a request, because one node
    /// cannot belong to two request bodies.
    /// </summary>
    public JsonNode Format { get; }

    /// <summary>Loads the embedded schema, failing startup when the resource is missing.</summary>
    public static NluOutputSchema Load()
    {
        using var stream = typeof(NluOutputSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded structured-output schema '{ResourceName}' is missing from the "
                + "Intelligence assembly.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var bytes = buffer.ToArray();

        return new NluOutputSchema(
            Encoding.UTF8.GetString(bytes),
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
