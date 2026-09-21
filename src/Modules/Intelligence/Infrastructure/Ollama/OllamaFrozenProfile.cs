namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The frozen Controlled Demo Candidate configuration of docs/TECHNICAL.md section 8.2. The values
/// exist once, here and in the checked-in host configuration template, so the adapter and its startup
/// validation cannot drift apart. The configuration is frozen, not general: a different model or
/// runtime profile is a new documented decision rather than a deployment setting, which is why the
/// validator rejects an incompatible profile instead of accepting it silently.
/// </summary>
public static class OllamaFrozenProfile
{
    /// <summary>The only supported provider of this adapter.</summary>
    public const string Provider = "Ollama";

    /// <summary>The local Ollama endpoint of the Controlled Demo Candidate.</summary>
    public const string BaseUrl = "http://127.0.0.1:11434";

    /// <summary>The frozen measured model tag, including its quantization.</summary>
    public const string Model = "qwen3.5:2b-q4_K_M";

    /// <summary>The configured AI timeout, in seconds, that separates timeout from caller cancellation.</summary>
    public const int TimeoutSeconds = 20;

    /// <summary>The deterministic sampling temperature of a structured extraction task.</summary>
    public const double Temperature = 0;

    /// <summary>The application-level context target of docs/TECHNICAL.md section 8.2.</summary>
    public const int ContextTokens = 4096;
}
