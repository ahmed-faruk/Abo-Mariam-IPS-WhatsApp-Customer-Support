namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>What a failed attempt did to the queued message.</summary>
public enum QueueFailureOutcome
{
    /// <summary>The message is Failed and becomes claimable again once its <c>run_after</c> passes.</summary>
    RetryScheduled,

    /// <summary>The attempt limit is exhausted, so the message is DeadLettered and never claimed again.</summary>
    DeadLettered,
}
