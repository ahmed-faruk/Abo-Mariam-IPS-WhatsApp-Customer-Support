namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The Intelligence module's structured-NLU boundary of docs/TECHNICAL.md section 8.1. Callers send a
/// customer message and receive either an interpretation or a deterministic failure status; the
/// adapter behind the interface is replaceable, and no Ollama, Qwen, HTTP or wire type ever crosses it.
/// </summary>
public interface IAiNluClient
{
    /// <summary>
    /// Interprets one customer message. A caller cancellation is propagated as an
    /// <see cref="OperationCanceledException"/> rather than being converted into a fallback result,
    /// because only the caller knows whether the turn was abandoned.
    /// </summary>
    /// <param name="message">The current customer message.</param>
    /// <param name="context">The bounded context of the previous turn; never authoritative commercial facts.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken);
}
