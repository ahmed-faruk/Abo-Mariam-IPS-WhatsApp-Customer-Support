namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>The outcome of one transport send attempt.</summary>
public sealed record OutboundSendResult
{
    private OutboundSendResult(bool succeeded, string? providerMessageId, string? error)
    {
        Succeeded = succeeded;
        ProviderMessageId = providerMessageId;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? ProviderMessageId { get; }

    public string? Error { get; }

    /// <summary>The provider accepted the message.</summary>
    public static OutboundSendResult Sent(string providerMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        return new OutboundSendResult(true, providerMessageId, null);
    }

    /// <summary>The provider or the network refused the attempt; the Outbox retries it.</summary>
    public static OutboundSendResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new OutboundSendResult(false, null, error);
    }
}
