namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The outcome of asking a renderer for the final customer-facing text of one response intent. A
/// renderer that is not bound yet returns <see cref="NotRendered"/>, which is why nothing is enqueued.
/// </summary>
public sealed class ConversationRenderResult
{
    private ConversationRenderResult(string? body)
    {
        Body = body;
    }

    /// <summary>The exact text to store in the Outbox, or null when nothing is rendered.</summary>
    public string? Body { get; }

    /// <summary>True when the renderer produced text that may be enqueued.</summary>
    public bool IsRendered => !string.IsNullOrWhiteSpace(Body);

    /// <summary>The final text of one reply. The body is immutable once the Outbox stores it.</summary>
    public static ConversationRenderResult Rendered(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new ConversationRenderResult(body);
    }

    /// <summary>
    /// No text was produced, so no Outbox row may be created. This is the documented behaviour of the
    /// unbound renderer the host runs until the deterministic renderer of Issue #12 is bound.
    /// </summary>
    public static ConversationRenderResult NotRendered() => new(null);
}
