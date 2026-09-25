namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The controlled-demo conversation reset of docs/TECHNICAL.md section 36.5. It removes named demo
/// customers, and with them their conversations and conversation state, so the next demo run starts
/// with no carried reference, shortlist or Human mode. Only the demo operator tooling registers this
/// contract; the production composition root never does.
/// </summary>
public interface IDemoConversationData
{
    /// <summary>Counts the stored conversations, of any mode, of the given customers.</summary>
    Task<int> CountConversationsAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the given customers; their conversations and conversation state go with them. Returns
    /// how many customers were deleted.
    /// </summary>
    Task<int> DeleteCustomersAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default);
}
