using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The renderer the host runs until the deterministic renderer of Issue #12 is bound. It is
/// deliberately fail-closed: it writes no customer-facing text at all, so the orchestration records the
/// turn and enqueues nothing, and no prototype wording can leak into a client conversation.
/// </summary>
public sealed class FailClosedConversationRenderer : IConversationRenderer
{
    public Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return Task.FromResult(ConversationRenderResult.NotRendered());
    }
}
