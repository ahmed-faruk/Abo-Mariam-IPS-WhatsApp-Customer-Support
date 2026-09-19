namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Maps harness failures onto the documented exit codes:
/// 0 = completed (gate PASS or FAIL), 1 = usage, 2 = invalid benchmark data,
/// 3 = Ollama/infrastructure failure (including a warm-up that never produced schema-valid output).
/// </summary>
public static class BenchmarkEntryPoint
{
    public static async Task<int> RunAsync(
        string[] args,
        BenchmarkServices services,
        CancellationToken cancellationToken)
    {
        try
        {
            var invocation = CommandLine.Parse(args);
            return await BenchmarkCommands.ExecuteAsync(invocation, services, cancellationToken).ConfigureAwait(false);
        }
        catch (UsageException exception)
        {
            services.Error.WriteLine($"error: {exception.Message}");
            services.Error.WriteLine();
            services.Error.WriteLine(CommandLine.Usage);
            return ExitCodes.UsageError;
        }
        catch (BenchmarkDataException exception)
        {
            services.Error.WriteLine($"invalid benchmark data: {exception.Message}");
            return ExitCodes.InvalidBenchmarkData;
        }
        catch (WarmupException exception)
        {
            services.Error.WriteLine($"warm-up failed: {exception.Message}");
            return ExitCodes.InfrastructureFailure;
        }
        catch (OllamaTransportException exception)
        {
            services.Error.WriteLine($"ollama failure: {exception.Message}");
            return ExitCodes.InfrastructureFailure;
        }
    }
}
