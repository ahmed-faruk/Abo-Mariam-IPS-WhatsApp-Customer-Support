namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>The outcome of one Ollama chat call, with the model reply when there was one.</summary>
public sealed record OllamaChatTransportResult
{
    /// <summary>What the call produced.</summary>
    public required OllamaChatOutcome Outcome { get; init; }

    /// <summary>The <c>message.content</c> string, present exactly when a reply was received.</summary>
    public string? Content { get; init; }

    /// <summary>A reply the model actually produced.</summary>
    public static OllamaChatTransportResult ReplyReceived(string content) => new()
    {
        Outcome = OllamaChatOutcome.ReplyReceived,
        Content = content,
    };

    /// <summary>The configured AI timeout elapsed before a reply arrived.</summary>
    public static OllamaChatTransportResult TimedOut() => new() { Outcome = OllamaChatOutcome.TimedOut };

    /// <summary>No usable reply arrived: unreachable, non-success status or malformed envelope.</summary>
    public static OllamaChatTransportResult Unavailable() => new() { Outcome = OllamaChatOutcome.Unavailable };
}
