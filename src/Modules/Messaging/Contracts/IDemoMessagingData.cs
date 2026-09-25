namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// The controlled-demo Messaging reset of docs/TECHNICAL.md section 36.5. It removes the Inbox and
/// Outbox rows of named demo customers, but only when none of that work is still open, so a reset can
/// never discard a message a worker would still process or send. Only the demo operator tooling
/// registers this contract; the production composition root never does.
/// </summary>
public interface IDemoMessagingData
{
    /// <summary>Counts the Inbox and Outbox rows of the given customers and how many are still open.</summary>
    Task<DemoMessagingCounts> CountAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction, refuses and deletes nothing when any Inbox or Outbox row of the given
    /// customers is Pending, Claimed or Failed. Otherwise deletes their Outbox rows, then their Inbox
    /// rows, then only those webhook envelopes of the deleted Inbox rows that no Inbox row references
    /// any more.
    /// </summary>
    Task<DemoMessagingDeleteResult> DeleteAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default);
}

/// <summary>The stored Messaging rows of a set of customers.</summary>
/// <param name="Inbox">The Inbox rows.</param>
/// <param name="Outbox">The Outbox rows.</param>
/// <param name="NonTerminal">The Inbox and Outbox rows that are Pending, Claimed or Failed.</param>
public sealed record DemoMessagingCounts(int Inbox, int Outbox, int NonTerminal);

/// <summary>What a demo Messaging delete did.</summary>
/// <param name="Refused">True when open work existed, in which case nothing was deleted.</param>
/// <param name="Inbox">The deleted Inbox rows.</param>
/// <param name="Outbox">The deleted Outbox rows.</param>
/// <param name="Envelopes">The deleted webhook envelopes.</param>
public sealed record DemoMessagingDeleteResult(bool Refused, int Inbox, int Outbox, int Envelopes);
