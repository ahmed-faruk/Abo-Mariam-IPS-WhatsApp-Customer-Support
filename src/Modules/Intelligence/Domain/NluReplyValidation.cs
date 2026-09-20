using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

/// <summary>
/// The outcome of validating one model reply: either a complete <see cref="NluInterpretation"/> or the
/// path-based problems that explain why the reply does not satisfy the NLU contract.
/// </summary>
public sealed record NluReplyValidation
{
    /// <summary>True when the reply satisfied every schema and semantic rule.</summary>
    public required bool IsValid { get; init; }

    /// <summary>The mapped interpretation, present exactly when <see cref="IsValid"/> is true.</summary>
    public NluInterpretation? Interpretation { get; init; }

    /// <summary>The failing field paths, in the order the validator found them.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>A reply that satisfies the contract.</summary>
    public static NluReplyValidation Valid(NluInterpretation interpretation) => new()
    {
        IsValid = true,
        Interpretation = interpretation,
    };

    /// <summary>A reply that does not satisfy the contract, with the reasons.</summary>
    public static NluReplyValidation Invalid(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        return new NluReplyValidation
        {
            IsValid = false,
            Problems = [.. problems.Select(NluDiagnostics.ClampProblem)],
        };
    }
}
