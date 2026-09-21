using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The persistence the inbound-turn orchestration needs, expressed as the handful of decisions it
/// makes. The implementation owns the Conversations schema and the race-free creation of a customer
/// and of their one active conversation.
/// </summary>
internal interface IConversationTurnStore
{
    /// <summary>
    /// Loads the customer, creating them on first contact, and returns their active conversation,
    /// creating a new AI conversation when the customer has none. A closed conversation is never
    /// reopened, so a customer whose last conversation was closed gets a new active one.
    /// </summary>
    Task<ConversationTurnContext> OpenTurnAsync(
        string customerExternalId,
        long? knownConversationId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>The stored UX state, or empty state when it is missing, malformed or expired.</summary>
    Task<ConversationStateDocument> LoadStateAsync(
        long conversationId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes the conversation's final-operation lock and returns the handle that owns it. Everything
    /// that reads the current mode and then acts on it - the last authorization of an automatic reply,
    /// and every explicit mode change - runs inside this one serialized section, so two replicas can
    /// never disagree about who owns the conversation. The handle releases the lock when it is
    /// committed or disposed.
    /// </summary>
    Task<IConversationOperation> BeginFinalOperationAsync(
        long conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the accepted inbound: the lifecycle timestamps and the 24-hour service window refreshed
    /// from the message's provider timestamp.
    /// </summary>
    Task AcceptInboundAsync(
        ConversationTurnContext context,
        DateTime providerTimestampUtc,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>Queues the state write with its sliding 30-minute expiry.</summary>
    Task SaveStateAsync(
        ConversationTurnContext context,
        ConversationStateDocument state,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>Queues a durable mode change.</summary>
    Task SetModeAsync(
        ConversationTurnContext context,
        string mode,
        DateTime utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits the recorded lifecycle and any queued state or mode change of this turn. This commit
    /// finishes before Messaging is asked for anything, because no transaction spans two modules.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Records that a reply of this conversation was accepted by the durable Outbox.</summary>
    Task RecordOutboundAsync(long conversationId, DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>
/// One serialized Conversations operation on a single conversation. The lock it holds is released by the
/// commit, and by the disposal that always follows it, so a failed or abandoned operation can never leave
/// the conversation locked.
/// </summary>
internal interface IConversationOperation : IAsyncDisposable
{
    /// <summary>
    /// Reads the conversation mode as it is stored right now, not as this turn captured it when it
    /// started. The answer is the final authorization: it decides whether an automatic reply may still
    /// be produced.
    /// </summary>
    Task<string> ReloadModeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits this operation's Conversations changes and releases the lock. A handle that is disposed
    /// without being committed rolls its changes back.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
