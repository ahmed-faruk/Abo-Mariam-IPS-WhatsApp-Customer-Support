using System.Collections.Immutable;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The bounded conversation context a caller may pass with the current message. It carries only the
/// minimal reference material a later ticket needs to resolve a follow-up such as "the first one":
/// the labels of the candidates the previous turn showed, plus the reference that turn resolved.
/// It never carries authoritative commercial facts.
/// </summary>
/// <remarks>
/// Issue #10 accepts the context but does not send it to the model. The frozen prompt of
/// docs/TECHNICAL.md section 8.2 (nlu-system-prompt-v3) states that the task sends a single text turn
/// with no conversation history, so changing the request to inject context would invalidate the
/// measured prompt identity. Issue #11 must resolve references with the frozen-compatible mechanism
/// available at that time instead of silently rewriting the request.
/// </remarks>
public sealed record NluConversationContext
{
    /// <summary>The largest number of previous candidate labels a caller may supply.</summary>
    public const int MaxCandidateLabels = 8;

    /// <summary>The largest length of one candidate label or previous reference.</summary>
    public const int MaxLabelLength = 120;

    private readonly ImmutableArray<string> _previousCandidateLabels = [];

    /// <summary>The context of a first turn: no previous candidates and no previous reference.</summary>
    public static NluConversationContext Empty { get; } = new();

    /// <summary>
    /// The labels of the candidates the previous turn showed, in the order it showed them. The initializer
    /// takes an immutable snapshot of the caller's collection, so mutating that collection afterwards —
    /// or casting the property back to a mutable list — cannot widen the context past its bounds.
    /// </summary>
    /// <exception cref="ArgumentException">The caller passed null instead of an empty collection.</exception>
    public IReadOnlyList<string> PreviousCandidateLabels
    {
        get => _previousCandidateLabels;
        init
        {
            if (value is null)
            {
                throw new ArgumentException(
                    $"{nameof(PreviousCandidateLabels)} must be an empty list rather than null.",
                    nameof(PreviousCandidateLabels));
            }

            var snapshot = new string[value.Count];

            for (var index = 0; index < snapshot.Length; index++)
            {
                snapshot[index] = value[index];
            }

            _previousCandidateLabels = ImmutableArray.Create(snapshot);
        }
    }

    /// <summary>The reference the previous turn resolved, when it resolved one.</summary>
    public string? PreviousReference { get; init; }

    /// <summary>True when the context carries nothing about a previous turn.</summary>
    public bool IsEmpty =>
        PreviousCandidateLabels.Count == 0 && string.IsNullOrWhiteSpace(PreviousReference);

    /// <summary>
    /// Rejects a context outside the documented bounds, so an oversized or blank context can never be
    /// carried into a later request.
    /// </summary>
    /// <exception cref="ArgumentException">The context exceeds <see cref="MaxCandidateLabels"/> or a label exceeds <see cref="MaxLabelLength"/>.</exception>
    public void EnsureWithinBounds()
    {
        if (PreviousCandidateLabels.Count > MaxCandidateLabels)
        {
            throw new ArgumentException(
                $"{nameof(PreviousCandidateLabels)} may hold at most {MaxCandidateLabels} labels, but held "
                + $"{PreviousCandidateLabels.Count}.",
                nameof(PreviousCandidateLabels));
        }

        foreach (var label in PreviousCandidateLabels)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > MaxLabelLength)
            {
                throw new ArgumentException(
                    $"A previous candidate label must be non-blank and at most {MaxLabelLength} characters.",
                    nameof(PreviousCandidateLabels));
            }
        }

        if (PreviousReference is { } reference && reference.Length > MaxLabelLength)
        {
            throw new ArgumentException(
                $"{nameof(PreviousReference)} must be at most {MaxLabelLength} characters.",
                nameof(PreviousReference));
        }
    }
}
