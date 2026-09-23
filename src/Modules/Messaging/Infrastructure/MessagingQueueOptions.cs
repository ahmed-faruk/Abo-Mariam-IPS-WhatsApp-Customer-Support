namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Queue policy for the Inbox/Outbox workers. The Inbox attempt limit lives here because
/// docs/TECHNICAL.md defines an attempt limit column only for the Outbox.
/// </summary>
public sealed class MessagingQueueOptions
{
    /// <summary>Messages one Inbox claim may take. The claim is always bounded by this value.</summary>
    public int InboxBatchSize { get; set; } = 20;

    /// <summary>
    /// Messages one Outbox poll may send. The worker claims each one immediately before its own send,
    /// so a poll never holds more than one live lease and this value is the per-poll send budget.
    /// </summary>
    public int OutboxBatchSize { get; set; } = 20;

    /// <summary>Attempts an Inbox message gets before it is DeadLettered.</summary>
    public int InboxMaxAttempts { get; set; } = 5;

    /// <summary>Delay before a failed Inbox message becomes claimable again.</summary>
    public TimeSpan InboxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delay before a failed Outbox message becomes claimable again.</summary>
    public TimeSpan OutboxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest retry delay any Outbox failure may schedule, including a provider retry hint. The
    /// durable retry schedule stays bounded, so one provider hint can never park the oldest message of
    /// a partition far into the future.
    /// </summary>
    public TimeSpan OutboxMaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a claim owns its message. A worker that crashes, is cancelled or loses the database
    /// keeps the partition only until this lease expires; after that the message is recovered and can
    /// be claimed again. The lease is enforced by PostgreSQL, so it holds for every replica.
    /// </summary>
    public TimeSpan ClaimLeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Idle wait between polls, so a worker with no work never busy-spins.</summary>
    public TimeSpan IdlePollDelay { get; set; } = TimeSpan.FromSeconds(1);
}
