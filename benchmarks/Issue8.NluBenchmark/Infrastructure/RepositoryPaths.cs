namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Resolves the repository locations the harness reads (dataset, schema, manifest) and
/// writes (results, reports). The repository root is the directory holding the solution
/// file, discovered from the running assembly so any working directory works.
/// </summary>
public sealed class RepositoryPaths
{
    public const string SolutionFileName = "WhatsAppMonitorAssistant.slnx";
    public const string BenchmarkRelativePath = "benchmarks/Issue8.NluBenchmark";

    private RepositoryPaths(string repositoryRoot)
    {
        RepositoryRoot = repositoryRoot;
        BenchmarkRoot = Path.Combine(repositoryRoot, "benchmarks", "Issue8.NluBenchmark");
    }

    public string RepositoryRoot { get; }

    public string BenchmarkRoot { get; }

    public string DatasetFile => Path.Combine(BenchmarkRoot, "data", "v1", "cases.jsonl");

    public string SchemaFile => Path.Combine(BenchmarkRoot, "schemas", "nlu-output.schema.json");

    public string ManifestFile => Path.Combine(BenchmarkRoot, "manifest.json");

    public string ResultsDirectory => Path.Combine(BenchmarkRoot, "results");

    public string ReportsDirectory => Path.Combine(BenchmarkRoot, "reports");

    public static RepositoryPaths Discover() =>
        ForRepositoryRoot(FindRepositoryRoot(AppContext.BaseDirectory)
            ?? FindRepositoryRoot(Directory.GetCurrentDirectory())
            ?? throw new BenchmarkDataException(
                $"{SolutionFileName} was not found above the harness assembly or the current directory."));

    public static RepositoryPaths ForRepositoryRoot(string repositoryRoot) => new(repositoryRoot);

    public static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
