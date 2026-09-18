namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The durable Inbox work queue. Claiming is exclusive and ordered per partition,
/// so a single demo worker behaves exactly like several later replicas.
/// </summary>
public interface IInboxMessageStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due Pending messages in one transaction using
    /// <c>FOR UPDATE SKIP LOCKED</c>. At most one message per partition is claimed, and the message
    /// of a partition is always the oldest nonterminal one, so a later message never overtakes an
    /// earlier Pending, Claimed or Failed message of the same partition. Partitions that are held by
    /// another claim transaction are skipped instead of blocking the rest of the queue.
    /// </summary>
    /// <remarks>
    /// The claim is leased: the returned messages carry a claim token, and an unexpired lease is the
    /// only thing that can hold a partition. A claim whose lease expires because its owner crashed,
    /// was cancelled or lost the database becomes claimable again, so abandoned work can never block
    /// a partition forever. Attempts are incremented by the claim itself, never by a completion.
    /// </remarks>
    Task<IReadOnlyList<ClaimedInboxMessage>> ClaimAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed message as Processed. The claim token must still own the claim; anything else
    /// is rejected with <see cref="ClaimOwnershipLostException"/> and changes nothing.
    /// </summary>
    Task CompleteAsync(
        long inboxMessageId,
        Guid claimToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the failure. The message becomes Failed with a retry schedule until the configured
    /// attempt limit is reached, and DeadLettered after that. The claim token must still own the
    /// claim; anything else is rejected with <see cref="ClaimOwnershipLostException"/> and changes
    /// nothing.
    /// </summary>
    Task<QueueFailureOutcome> FailAsync(
        long inboxMessageId,
        Guid claimToken,
        string error,
        CancellationToken cancellationToken = default);
}
