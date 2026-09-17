namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The durable Outbox work queue. Claiming is exclusive and ordered per partition, so a single
/// demo worker behaves exactly like several later replicas.
/// </summary>
public interface IOutboxMessageStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due Pending messages in one transaction using
    /// <c>FOR UPDATE SKIP LOCKED</c>. At most one message per partition is claimed, oldest id
    /// first, and partitions that already hold a claim are skipped.
    /// </summary>
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a claimed message as Sent with the provider message id of the send.</summary>
    Task CompleteAsync(
        long outboxMessageId,
        string providerMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the failure. The message becomes Failed with a retry schedule until its stored
    /// attempt limit is reached, and DeadLettered after that.
    /// </summary>
    Task<QueueFailureOutcome> FailAsync(
        long outboxMessageId,
        string error,
        CancellationToken cancellationToken = default);
}
