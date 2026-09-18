namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>
/// A completion or a failure was presented by an owner that no longer holds the claim, because the
/// lease expired and the row was recovered by somebody else, or because the row already reached a
/// different terminal state. The stale owner must not overwrite the current owner's bookkeeping.
/// </summary>
public sealed class ClaimOwnershipLostException : Exception
{
    public ClaimOwnershipLostException()
    {
    }

    public ClaimOwnershipLostException(string message)
        : base(message)
    {
    }

    public ClaimOwnershipLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
