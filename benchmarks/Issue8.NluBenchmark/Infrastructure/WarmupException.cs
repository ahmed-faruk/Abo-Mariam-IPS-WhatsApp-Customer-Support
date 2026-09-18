namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Raised when a warm-up request cannot produce schema-valid structured output. Warm-up is the
/// gate that proves the model is usable before a measured pass, so this is an infrastructure
/// failure: the harness must not claim the model is warm and must not continue to run1.
/// </summary>
public sealed class WarmupException : Exception
{
    public WarmupException(string message)
        : base(message)
    {
    }
}
