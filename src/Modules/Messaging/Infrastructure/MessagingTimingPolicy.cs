namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// The timing budgets the durable Inbox/Outbox workers actually enforce, and the single owner of the
/// arithmetic that relates them to a claim lease. Every value here is applied by production code, and
/// the startup validators read these same values, so a configuration that cannot satisfy them fails
/// at startup instead of being silently tolerated by an unenforced margin.
/// </summary>
/// <param name="ClaimBudget">
/// The overall elapsed-time budget of one complete Outbox claim path, enforced by the worker around
/// the whole claim before any provider send may start. It is not a per-statement limit: the claim path
/// opens a connection, begins a transaction, runs the retry requeue, the expired-claim recovery and the
/// claim statement, reads the returned row and commits, so it is several sequential database
/// operations rather than one command.
/// </param>
/// <param name="CompletionBookkeepingBudget">
/// The whole budget recording one accepted delivery may spend, shared by every bounded completion
/// attempt. It is not a per-attempt timeout: three attempts do not get three budgets.
/// </param>
/// <param name="DatabaseCommandTimeout">
/// The explicit finite timeout of one individual durable queue statement. It is applied by
/// <c>MessagingQueueCommands.Create</c> so a connection-string <c>Command Timeout</c> can never make
/// a queue command unbounded. Cancellation tokens may still stop a command earlier.
/// </param>
/// <param name="LeaseSafetySlack">
/// The gap the local lease guard keeps before the database lease expires. The guard is what makes the
/// safety property hold: the guard starts before the claim, so it always expires first.
/// </param>
internal sealed record MessagingTimingPolicy(
    TimeSpan ClaimBudget,
    TimeSpan CompletionBookkeepingBudget,
    TimeSpan DatabaseCommandTimeout,
    TimeSpan LeaseSafetySlack)
{
    /// <summary>
    /// The policy the workers run with. The claim path runs at most three bounded queue statements -
    /// the due-retry requeue, the expired-claim recovery and the claim itself - each capped at
    /// <see cref="DatabaseCommandTimeout"/>, and it also opens the connection and commits the
    /// transaction, so a 30-second claim budget is twice that worst-case command time and still leaves
    /// an order of magnitude of headroom inside the default five-minute claim lease.
    /// </summary>
    public static MessagingTimingPolicy Default { get; } = new(
        ClaimBudget: TimeSpan.FromSeconds(30),
        CompletionBookkeepingBudget: TimeSpan.FromSeconds(20),
        DatabaseCommandTimeout: TimeSpan.FromSeconds(5),
        LeaseSafetySlack: TimeSpan.FromSeconds(10));

    /// <summary>
    /// The longest deadline a cancellation token can be scheduled for, about 49.7 days on .NET. The
    /// lease guard is a scheduled cancellation, so a lease that derives a longer deadline than this is
    /// rejected at startup instead of failing every poll.
    /// </summary>
    public static TimeSpan MaxClaimLease { get; } = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// The local deadline of one claimed item, measured from immediately before the claim itself. It
    /// is always strictly shorter than the database lease, so a worker that honours it stops sending,
    /// completing and failing before any other replica may legally recover the same claim.
    /// </summary>
    public TimeSpan LeaseSafetyDeadline(TimeSpan claimLease) => claimLease - LeaseSafetySlack;

    /// <summary>
    /// The shortest claim lease one normal item needs: the bounded overall claim path, the whole
    /// provider attempt, the shared completion-bookkeeping budget and the lease-safety slack. The
    /// startup validators compare the configured lease against this, while the lease guard is the hard
    /// mechanism that holds at runtime whatever the configuration says.
    /// </summary>
    public TimeSpan RequiredClaimLease(int providerTimeoutSeconds) =>
        ClaimBudget
        + TimeSpan.FromSeconds(providerTimeoutSeconds)
        + CompletionBookkeepingBudget
        + LeaseSafetySlack;
}
