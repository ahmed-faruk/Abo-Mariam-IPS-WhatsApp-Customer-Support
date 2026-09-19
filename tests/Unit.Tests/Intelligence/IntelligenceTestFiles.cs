using System.Security.Cryptography;
using System.Text;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>Locates the repository files the Intelligence tests pin by hash.</summary>
internal static class IntelligenceTestFiles
{
    private const string SolutionFileName = "WhatsAppMonitorAssistant.slnx";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public const string ProductionSchemaPath =
        "src/Modules/Intelligence/Infrastructure/Ollama/Resources/nlu-output.schema.json";

    public const string BenchmarkSchemaPath =
        "benchmarks/Issue8.NluBenchmark/schemas/nlu-output.schema.json";

    public static string Sha256OfFile(string relativePath) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(RepositoryRoot, relativePath))));

    public static string Sha256OfUtf8(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }
}
