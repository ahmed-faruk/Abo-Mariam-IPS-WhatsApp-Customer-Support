namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The durable Outbox work queue. Claiming is exclusive and ordered per partition, so a single
/// demo worker behaves exactly like several later replicas.
/// </summary>
public interface IOutboxMessageStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due Pending messages in one transaction using
    /// <c>FOR UPDATE SKIP LOCKED</c>. At most one message per partition is claimed, and the message
    /// of a partition is always the oldest nonterminal one, so a later reply never overtakes an
    /// earlier Pending, Claimed or Failed reply of the same partition. Partitions that are held by
    /// another claim transaction are skipped instead of blocking the rest of the queue.
    /// </summary>
    /// <remarks>
    /// The claim is leased: the returned messages carry a claim token, and an unexpired lease is the
    /// only thing that can hold a partition. A claim whose lease expires because its owner crashed,
    /// was cancelled or lost the database becomes claimable again, so abandoned work can never block
    /// a partition forever. Attempts are incremented by the claim itself, never by a completion.
    /// </remarks>
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed message as Sent with the provider message id of the accepted delivery. The
    /// claim token must still own the claim, otherwise the call is rejected with
    /// <see cref="ClaimOwnershipLostException"/> and changes nothing.
    /// </summary>
    /// <remarks>
    /// Recording a delivery is idempotent for the same successful provider result: once the message
    /// is Sent with this <paramref name="providerMessageId"/>, repeating the call is a no-op instead
    /// of a conflict. That is what lets a worker reconcile a completion whose database response was
    /// lost, without ever treating the already accepted delivery as a failed send.
    /// </remarks>
    Task CompleteAsync(
        long outboxMessageId,
        Guid claimToken,
        string providerMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the failure. The message becomes Failed with a retry schedule until its stored
    /// attempt limit is reached, and DeadLettered after that. The claim token must still own the
    /// claim; anything else is rejected with <see cref="ClaimOwnershipLostException"/> and changes
    /// nothing.
    /// </summary>
    Task<QueueFailureOutcome> FailAsync(
        long outboxMessageId,
        Guid claimToken,
        string error,
        CancellationToken cancellationToken = default);
}
