namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Queue policy for the Inbox/Outbox workers. The Inbox attempt limit lives here because
/// docs/TECHNICAL.md defines an attempt limit column only for the Outbox.
/// </summary>
public sealed class MessagingQueueOptions
{
    /// <summary>Messages one Inbox claim may take. The claim is always bounded by this value.</summary>
    public int InboxBatchSize { get; set; } = 20;

    /// <summary>Messages one Outbox claim may take. The claim is always bounded by this value.</summary>
    public int OutboxBatchSize { get; set; } = 20;

    /// <summary>Attempts an Inbox message gets before it is DeadLettered.</summary>
    public int InboxMaxAttempts { get; set; } = 5;

    /// <summary>Delay before a failed Inbox message becomes claimable again.</summary>
    public TimeSpan InboxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delay before a failed Outbox message becomes claimable again.</summary>
    public TimeSpan OutboxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a claim owns its message. A worker that crashes, is cancelled or loses the database
    /// keeps the partition only until this lease expires; after that the message is recovered and can
    /// be claimed again. The lease is enforced by PostgreSQL, so it holds for every replica.
    /// </summary>
    public TimeSpan ClaimLeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Idle wait between polls, so a worker with no work never busy-spins.</summary>
    public TimeSpan IdlePollDelay { get; set; } = TimeSpan.FromSeconds(1);
}
