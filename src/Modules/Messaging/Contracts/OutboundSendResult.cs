namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>The outcome of one transport send attempt.</summary>
public sealed record OutboundSendResult
{
    private OutboundSendResult(
        OutboundSendOutcome outcome,
        string? providerMessageId,
        string? error,
        TimeSpan? retryAfter)
    {
        Outcome = outcome;
        ProviderMessageId = providerMessageId;
        Error = error;
        RetryAfter = retryAfter;
    }

    public OutboundSendOutcome Outcome { get; }

    public bool Succeeded => Outcome == OutboundSendOutcome.Accepted;

    public string? ProviderMessageId { get; }

    public string? Error { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>The provider accepted the message and returned its provider message id.</summary>
    public static OutboundSendResult Sent(string providerMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        return new OutboundSendResult(OutboundSendOutcome.Accepted, providerMessageId, null, null);
    }

    /// <summary>The attempt is known unsuccessful and may be retried by the durable Outbox policy.</summary>
    public static OutboundSendResult RetryableFailure(string error, TimeSpan? retryAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        if (retryAfter is not null && retryAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter), retryAfter, "The retry delay must be positive.");
        }

        return new OutboundSendResult(OutboundSendOutcome.RetryableFailure, null, error, retryAfter);
    }

    /// <summary>The unchanged request cannot succeed automatically and should be terminal.</summary>
    public static OutboundSendResult PermanentFailure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new OutboundSendResult(OutboundSendOutcome.PermanentFailure, null, error, null);
    }

    /// <summary>The application cannot prove whether the provider accepted the attempt.</summary>
    public static OutboundSendResult Unknown(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new OutboundSendResult(OutboundSendOutcome.Unknown, null, error, null);
    }

    /// <summary>Compatibility alias for older tests that meant a retryable failed attempt.</summary>
    public static OutboundSendResult Failed(string error)
        => RetryableFailure(error);
}

public enum OutboundSendOutcome
{
    Accepted,
    RetryableFailure,
    PermanentFailure,
    Unknown,
}
