namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The model-neutral result of one analysis. A success carries the <see cref="NluInterpretation"/>; a
/// failure carries only a safe status and, for invalid model output, the validation problems. It never
/// carries raw model content, HTTP bodies or exception messages, so a caller cannot mistake any of
/// them for a commercial fact.
/// </summary>
public sealed record NluAnalysisResult
{
    /// <summary>The outcome of the analysis.</summary>
    public required NluAnalysisStatus Status { get; init; }

    /// <summary>The interpretation, present exactly when <see cref="Status"/> is <see cref="NluAnalysisStatus.Success"/>.</summary>
    public NluInterpretation? Interpretation { get; init; }

    /// <summary>
    /// Path-based validation problems of the final model reply, in the order the validator found them.
    /// They name the failing fields and never repeat the model's values.
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>
    /// True when the caller should ask the customer for a clarification instead of answering: the model
    /// replied twice without producing output that satisfies the NLU contract.
    /// </summary>
    public bool RequiresClarification => Status == NluAnalysisStatus.InvalidModelOutput;

    /// <summary>A successful analysis carrying the mapped interpretation.</summary>
    public static NluAnalysisResult Success(NluInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(interpretation);

        return new NluAnalysisResult
        {
            Status = NluAnalysisStatus.Success,
            Interpretation = interpretation,
        };
    }

    /// <summary>The safe result after the model's last reply failed NLU validation.</summary>
    public static NluAnalysisResult InvalidModelOutput(IReadOnlyList<string> problems) => new()
    {
        Status = NluAnalysisStatus.InvalidModelOutput,
        Problems = [.. problems],
    };

    /// <summary>The safe result when Ollama is unreachable or returned an unusable envelope.</summary>
    public static NluAnalysisResult AiUnavailable() => new() { Status = NluAnalysisStatus.AiUnavailable };

    /// <summary>The safe result when the configured AI timeout elapsed before a reply arrived.</summary>
    public static NluAnalysisResult TimedOut() => new() { Status = NluAnalysisStatus.Timeout };
}
