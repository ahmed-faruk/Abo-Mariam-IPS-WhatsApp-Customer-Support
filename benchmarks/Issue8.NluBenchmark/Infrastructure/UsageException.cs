namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>Raised for an unknown command, an unknown option or an invalid option value.</summary>
public sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }
}
