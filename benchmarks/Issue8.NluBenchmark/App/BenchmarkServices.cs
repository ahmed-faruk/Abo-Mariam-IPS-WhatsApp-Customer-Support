namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Everything the commands need from the outside world, so the offline tests can drive the
/// real command flow without Ollama or a network.
/// </summary>
public sealed record BenchmarkServices
{
    public required RepositoryPaths Paths { get; init; }

    public required TextWriter Output { get; init; }

    public required TextWriter Error { get; init; }

    public Func<string, string, int, IOllamaGateway> CreateGateway { get; init; } =
        (_, baseUrl, timeoutSeconds) => new OllamaNluClient(
            new HttpClient(),
            new OllamaOptions { BaseUrl = baseUrl, TimeoutSeconds = timeoutSeconds });

    public Func<BenchmarkDataset, IOllamaGateway> CreateFixtureGateway { get; init; } = _ => new FixtureNluGateway();

    /// <summary>
    /// Builds the artifact store for one directory. Commands never construct the store directly,
    /// so a test can observe or redirect run persistence, report loading and no-overwrite
    /// behaviour without going near Ollama.
    /// </summary>
    public Func<string, RunArtifactStore> CreateArtifactStore { get; init; } = directory => new RunArtifactStore(directory);

    public EnvironmentMetadataCollector EnvironmentCollector { get; init; } = new();

    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public static BenchmarkServices ForConsole() => new()
    {
        Paths = RepositoryPaths.Discover(),
        Output = Console.Out,
        Error = Console.Error,
        CreateArtifactStore = directory => new RunArtifactStore(directory),
    };
}
