using System.Text.Json;
using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>Shared access to the committed benchmark inputs and to temp copies for command tests.</summary>
internal static class BenchmarkFixtures
{
    private const string SolutionFileName = "WhatsAppMonitorAssistant.slnx";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static RepositoryPaths Paths { get; } = RepositoryPaths.ForRepositoryRoot(RepositoryRoot);

    public static BenchmarkManifest Manifest { get; } = BenchmarkManifest.Load(Paths);

    public static BenchmarkDataset Dataset { get; } = BenchmarkDataset.Load(Paths, Manifest);

    public static JsonSchemaValidator SchemaValidator { get; } = JsonSchemaValidator.FromFile(Paths.SchemaFile);

    public static IReadOnlyList<string> ValidateCommittedInputs() =>
        [.. Manifest.Validate(), .. Dataset.Validate(Manifest)];

    public static NluAnalyzer CreateAnalyzer(
        INluTransport transport,
        string model = "test-model",
        TimeProvider? timeProvider = null) =>
        new(
            transport,
            NluRequestBuilder.FromFile(Paths.SchemaFile),
            SchemaValidator,
            new NluRequestParameters { Model = model, Temperature = 0, ContextTokens = 4096 },
            timeProvider);

    public static BenchmarkCase CreateCase(string id, NluOutput expected, string input = "عايز شاشة") => new()
    {
        Id = id,
        Input = input,
        Expected = expected,
        Tags = ["test"],
    };

    public static NluOutput NoFilterSearch(string intent = "ProductSearch") => new()
    {
        Intent = intent,
        RequiredPorts = [],
        Grades = [],
        BudgetType = "None",
    };

    /// <summary>A throwaway repository root holding copies of the committed benchmark inputs.</summary>
    public static string CreateTempRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"issue8-benchmark-{Guid.NewGuid():N}");
        var benchmarkRoot = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark");
        Directory.CreateDirectory(Path.Combine(benchmarkRoot, "Data", "v1"));
        Directory.CreateDirectory(Path.Combine(benchmarkRoot, "schemas"));
        File.Copy(Paths.DatasetFile, Path.Combine(benchmarkRoot, "Data", "v1", "cases.jsonl"));
        File.Copy(Paths.SchemaFile, Path.Combine(benchmarkRoot, "schemas", "nlu-output.schema.json"));
        File.Copy(Paths.ManifestFile, Path.Combine(benchmarkRoot, "manifest.json"));
        return root;
    }

    public static BenchmarkServices CreateServices(string repositoryRoot, out StringWriter output)
    {
        output = new StringWriter();

        return new BenchmarkServices
        {
            Paths = RepositoryPaths.ForRepositoryRoot(repositoryRoot),
            Output = output,
            Error = new StringWriter(),
        };
    }

    public static string Serialize(NluOutput output) => JsonSerializer.Serialize(output, BenchmarkJson.Options);

    public static NluCaseExecution Execution(
        string caseId,
        NluOutput? output,
        bool schemaValid = true,
        bool retried = false,
        long milliseconds = 1000,
        string? failureReason = null,
        NluTransportTiming? timing = null,
        string? transportFailure = null)
    {
        NluAttempt Attempt(bool correction, long duration) => new()
        {
            Correction = correction,
            RawContent = output is null || transportFailure is not null ? string.Empty : Serialize(output),
            WallClockMilliseconds = duration,
            SchemaValid = schemaValid,
            SchemaErrors = schemaValid ? [] : ["$: required property 'budgetType' is missing"],
            TransportFailure = transportFailure,
            Output = schemaValid ? output : null,
            Timing = timing,
        };

        return new NluCaseExecution
        {
            CaseId = caseId,
            Attempts = retried
                ? [Attempt(false, milliseconds - 5), Attempt(true, 5)]
                : [Attempt(false, milliseconds)],
            SchemaValid = schemaValid,
            Output = schemaValid ? output : null,
            FailureReason = failureReason,
        };
    }

    /// <summary>Executions for every committed case, in file order, using the authored expectations.</summary>
    public static NluCaseExecution[] AllExpectedExecutions(long milliseconds = 1000) =>
        [.. Dataset.Cases.Select(testCase => Execution(testCase.Id, testCase.Expected, milliseconds: milliseconds))];

    /// <summary>A measured run that covers the whole dataset exactly once.</summary>
    public static RunArtifact CompleteRun(string runId, NluCaseExecution[]? executions = null) =>
        Run(runId, executions ?? AllExpectedExecutions());

    /// <summary>Attaches the stable machine/runtime identity a real run records.</summary>
    public static RunArtifact WithEnvironment(
        this RunArtifact run,
        string osVersion = "26.7",
        string architecture = "x86_64",
        string cpu = "Intel(R) Core(TM) i7-1068NG7 CPU @ 2.30GHz",
        long? memoryBytes = 17179869184,
        string? ollamaVersion = "0.34.2",
        string collectedAtUtc = "2026-01-01T00:00:00.0000000+00:00") => run with
        {
            Environment = new EnvironmentMetadata
            {
                OsVersion = osVersion,
                Architecture = architecture,
                Cpu = cpu,
                MemoryBytes = memoryBytes,
                OllamaVersion = ollamaVersion,
                CollectedAtUtc = collectedAtUtc,
            },
        };

    public static RunArtifact WithSettings(
        this RunArtifact run,
        int? temperature = null,
        int? contextTokens = null,
        int? timeoutSeconds = null,
        string? retryPolicy = null) => run with
        {
            Settings = run.Settings with
            {
                Temperature = temperature ?? run.Settings.Temperature,
                ContextTokens = contextTokens ?? run.Settings.ContextTokens,
                TimeoutSeconds = timeoutSeconds ?? run.Settings.TimeoutSeconds,
                RetryPolicy = retryPolicy ?? run.Settings.RetryPolicy,
            },
        };

    public static RunArtifact Run(string runId, params NluCaseExecution[] executions) =>
        RunArtifact.FromExecution(
            new RunContext
            {
                RunId = runId,
                Mode = RunModes.Live,
                Model = "test-model",
                BaseUrl = "http://127.0.0.1:11434",
                Settings = new RunSettings
                {
                    Temperature = 0,
                    ContextTokens = 4096,
                    TimeoutSeconds = 20,
                    RetryPolicy = BenchmarkManifest.RetryPolicyOneRetryMaximum,
                },
            },
            Dataset.Version,
            Dataset.Sha256,
            Manifest.Schema.Version,
            Manifest.Schema.Sha256,
            Manifest.Prompt.Version,
            "2026-01-01T00:00:00.0000000+00:00",
            "2026-01-01T00:05:00.0000000+00:00",
            executions);

    public static NluOutput Expected(string caseId) =>
        Dataset.Cases.Single(testCase => testCase.Id == caseId).Expected;

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

/// <summary>A gateway scripted with exact response payloads; the tests never touch Ollama.</summary>
internal sealed class ScriptedGateway : IOllamaGateway
{
    public const string TransportFailure = "\u0000transport-failure";

    private readonly Queue<string> _responses;

    public ScriptedGateway(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public List<NluTransportRequest> Requests { get; } = [];

    public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new OllamaHealth("test-ollama", ["test-model"]));

    public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The scripted gateway ran out of responses.");
        }

        var next = _responses.Dequeue();

        if (string.Equals(next, TransportFailure, StringComparison.Ordinal))
        {
            throw new OllamaTransportException("scripted transport failure");
        }

        return Task.FromResult(new NluTransportResponse { Content = next });
    }
}
