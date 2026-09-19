namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// What one Ollama chat call produced. Only <see cref="ReplyReceived"/> means the model actually
/// answered, and only such an answer may ever be corrected and retried.
/// </summary>
public enum OllamaChatOutcome
{
    /// <summary>The model answered with a <c>message.content</c> string.</summary>
    ReplyReceived,

    /// <summary>Ollama did not answer within the configured AI timeout.</summary>
    TimedOut,

    /// <summary>Ollama was unreachable, returned an error status, or returned an unusable envelope.</summary>
    Unavailable,
}
