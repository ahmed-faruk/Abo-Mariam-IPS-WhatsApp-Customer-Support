namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The Intelligence module configuration, bound from the <c>Ai</c> section of the host configuration
/// (docs/TECHNICAL.md section 8.2). There are no compiled-in defaults: an omitted value fails startup
/// validation instead of silently running a model configuration nobody measured.
/// </summary>
public sealed class OllamaAiOptions
{
    /// <summary>The configuration section the composition root binds this profile from.</summary>
    public const string ConfigurationSectionName = "Ai";

    /// <summary>The AI provider. The frozen Controlled Demo Candidate uses <c>Ollama</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The absolute base URL of the local Ollama runtime.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The exact model tag, including its quantization.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// How long one model request may take before it is a timeout rather than a caller cancellation.
    /// The adapter applies it with a linked token, so the caller's own cancellation stays distinct.
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>The deterministic sampling temperature.</summary>
    public double Temperature { get; set; }

    /// <summary>The application-level context target, sent as Ollama's <c>num_ctx</c>.</summary>
    public int ContextTokens { get; set; }
}
