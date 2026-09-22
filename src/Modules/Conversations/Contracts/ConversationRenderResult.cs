using System.Collections.Immutable;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The outcome of asking a renderer for one response intent: the final customer-facing text, and the
/// products that text really displays. A renderer that is not bound returns <see cref="NotRendered"/>,
/// which is why nothing is enqueued.
/// </summary>
public sealed class ConversationRenderResult
{
    private static readonly ImmutableArray<ConversationDisplayedCandidate> NoCandidates = [];

    private readonly ImmutableArray<ConversationDisplayedCandidate> displayed;

    private ConversationRenderResult(string? body, ImmutableArray<ConversationDisplayedCandidate> displayed)
    {
        Body = body;
        this.displayed = displayed;
    }

    /// <summary>The exact text to store in the Outbox, or null when nothing is rendered.</summary>
    public string? Body { get; }

    /// <summary>True when the renderer produced text that may be enqueued.</summary>
    public bool IsRendered => !string.IsNullOrWhiteSpace(Body);

    /// <summary>
    /// The products the rendered text displays, in the order the customer reads them, or an empty list
    /// when the reply displays no product. This is the only source of a displayed list, so the list a
    /// conversation may later reference is the list the customer was actually shown and never the
    /// candidates a turn merely intended to show.
    /// </summary>
    public IReadOnlyList<ConversationDisplayedCandidate> DisplayedCandidates => displayed;

    /// <summary>The final text of one reply that displays no product. The body is immutable once stored.</summary>
    public static ConversationRenderResult Rendered(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new ConversationRenderResult(body, NoCandidates);
    }

    /// <summary>
    /// The final text of one reply together with the ordered products it displays. The candidates are
    /// snapshotted, so a renderer that keeps its own collection cannot change them afterwards.
    /// </summary>
    public static ConversationRenderResult Rendered(
        string body,
        IEnumerable<ConversationDisplayedCandidate> displayedCandidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentNullException.ThrowIfNull(displayedCandidates);

        var candidates = displayedCandidates as IReadOnlyCollection<ConversationDisplayedCandidate>
            ?? [.. displayedCandidates];

        return new ConversationRenderResult(body, [.. candidates]);
    }

    /// <summary>
    /// No text was produced, so no Outbox row may be created. This is the documented behaviour of the
    /// unbound renderer the host runs until the deterministic renderer of Issue #12 is bound.
    /// </summary>
    public static ConversationRenderResult NotRendered() => new(null, NoCandidates);
}
