namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Process exit codes. A model quality gate failure is report data, not a crash, so a
/// completed run always exits 0.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int InvalidBenchmarkData = 2;
    public const int InfrastructureFailure = 3;
}
