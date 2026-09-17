namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The durable Inbox work queue. Claiming is exclusive and ordered per partition,
/// so a single demo worker behaves exactly like several later replicas.
/// </summary>
public interface IInboxMessageStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due Pending messages in one transaction using
    /// <c>FOR UPDATE SKIP LOCKED</c>. At most one message per partition is claimed, oldest id
    /// first, and partitions that already hold a claim are skipped.
    /// </summary>
    Task<IReadOnlyList<ClaimedInboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a claimed message as Processed.</summary>
    Task CompleteAsync(long inboxMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the failure. The message becomes Failed with a retry schedule until the configured
    /// attempt limit is reached, and DeadLettered after that.
    /// </summary>
    Task<QueueFailureOutcome> FailAsync(
        long inboxMessageId,
        string error,
        CancellationToken cancellationToken = default);
}
