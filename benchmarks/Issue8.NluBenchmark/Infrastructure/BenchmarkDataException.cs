namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Raised when the committed benchmark inputs are invalid: dataset, schema, manifest or
/// their hashes disagree. These are harness defects, not model results.
/// </summary>
public sealed class BenchmarkDataException : Exception
{
    public BenchmarkDataException(string message)
        : base(message)
    {
    }

    public BenchmarkDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
