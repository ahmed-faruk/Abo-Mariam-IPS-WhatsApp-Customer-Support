namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The model-neutral outcome of one structured-NLU analysis. The first three values below map to the
/// documented safe behaviour of docs/TECHNICAL.md section 30: an invalid model reply needs a
/// clarification or handoff, and unavailable or slow AI must never be answered with a guess.
/// </summary>
public enum NluAnalysisStatus
{
    /// <summary>A schema-valid and semantically valid interpretation was produced.</summary>
    Success,

    /// <summary>
    /// The model replied, but neither reply satisfied the NLU contract, so the caller must ask for a
    /// clarification or hand the conversation over. See <see cref="NluAnalysisResult.Problems"/>.
    /// </summary>
    InvalidModelOutput,

    /// <summary>Ollama could not be reached or did not answer with a usable reply, so nothing may be guessed.</summary>
    AiUnavailable,

    /// <summary>The model did not answer within the configured AI timeout.</summary>
    Timeout,
}
