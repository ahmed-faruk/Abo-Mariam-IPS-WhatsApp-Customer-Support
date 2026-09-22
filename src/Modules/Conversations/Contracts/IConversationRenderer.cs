namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The renderer port of docs/PLAN.md section 9: the application decides, the renderer writes the
/// customer-facing text from current authoritative facts. The deterministic implementation and its
/// production binding arrive with Issue #12; until then the host runs an unbound renderer.
/// </summary>
public interface IConversationRenderer
{
    /// <summary>
    /// Builds the final text of one response intent. The implementation reloads every changing fact it
    /// needs through the owning module contract, so no price, quantity or business answer is taken from
    /// the intent or from conversation state.
    /// </summary>
    Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default);
}
