using System.Globalization;
using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// The five commands. Quality gate failures never change the exit code: they are written into
/// the report. Only usage errors, invalid benchmark data and Ollama failures exit non-zero.
/// </summary>
public static class BenchmarkCommands
{
    public static async Task<int> ExecuteAsync(
        BenchmarkInvocation invocation,
        BenchmarkServices services,
        CancellationToken cancellationToken)
    {
        switch (invocation.Command)
        {
            case BenchmarkCommandKind.Help:
                services.Output.WriteLine(CommandLine.Usage);
                return ExitCodes.Success;
            case BenchmarkCommandKind.Validate:
                return ValidateCommand(services);
            case BenchmarkCommandKind.DryRun:
                return await DryRunCommandAsync(services, cancellationToken).ConfigureAwait(false);
            case BenchmarkCommandKind.Warmup:
                return await WarmupCommandAsync(invocation, services, cancellationToken).ConfigureAwait(false);
            case BenchmarkCommandKind.Run:
                return await RunCommandAsync(invocation, services, cancellationToken).ConfigureAwait(false);
            case BenchmarkCommandKind.Report:
                return ReportCommand(invocation, services);
            default:
                throw new UsageException($"Command '{invocation.Command}' is not implemented.");
        }
    }

    private static int ValidateCommand(BenchmarkServices services)
    {
        var (manifest, dataset) = LoadValidated(services);

        services.Output.WriteLine($"manifest: {manifest.BenchmarkFormatVersion} · harness {manifest.HarnessVersion}");
        services.Output.WriteLine($"source of truth: {manifest.SourceDocument}");
        services.Output.WriteLine($"dataset: {dataset.Version} · {dataset.Cases.Count} cases · sha256 {dataset.Sha256}");
        services.Output.WriteLine($"schema: {manifest.Schema.Version} · sha256 {manifest.Schema.Sha256} · validator keywords ok");
        services.Output.WriteLine($"prompt: {manifest.Prompt.Version}");

        var hardBudgetCases = dataset.Cases.Count(testCase => testCase.HardBudgetCase);
        var gatingCases = dataset.Cases.Count(testCase => testCase.Gating);
        var observationalCases = dataset.Cases.Count - gatingCases;

        services.Output.WriteLine($"cases: {gatingCases} gating, {observationalCases} observational, {hardBudgetCases} dedicated hard-budget");
        services.Output.WriteLine($"coverage tags: {dataset.Tags.Count} present, {manifest.RequiredCoverageTags.Length} required, all covered");
        services.Output.WriteLine($"expected outputs: all {dataset.Cases.Count} validate against the schema");
        services.Output.WriteLine($"retry policy: {manifest.DefaultCandidate.RetryPolicy}");

        return ExitCodes.Success;
    }

    private static async Task<int> DryRunCommandAsync(
        BenchmarkServices services,
        CancellationToken cancellationToken)
    {
        var (manifest, dataset) = LoadValidated(services);
        var runDirectory = Path.Combine(services.Paths.ResultsDirectory, "dry-run");
        var gateway = services.CreateFixtureGateway(dataset);
        var artifact = await ExecuteRunAsync(
            services,
            manifest,
            dataset,
            gateway,
            "dry-run-1",
            RunModes.DryRun,
            manifest.DefaultCandidate.Model,
            manifest.DefaultCandidate.BaseUrl,
            new OllamaHealth("offline-fixture", ["offline-fixture"]),
            cancellationToken).ConfigureAwait(false);

        new RunArtifactStore(runDirectory).Save(artifact);

        var evaluation = BenchmarkEvaluator.Evaluate(manifest, dataset, [artifact]);
        var outputs = BenchmarkReportWriter.Write(
            runDirectory,
            [artifact],
            evaluation,
            manifest,
            dataset,
            services.Clock);

        services.Output.WriteLine();
        services.Output.WriteLine(BenchmarkReportWriter.DryRunBanner);
        PrintEvaluation(services, evaluation);
        services.Output.WriteLine($"dry-run artifacts: {outputs.MarkdownPath}, {outputs.JsonPath}");
        services.Output.WriteLine("No live inference was performed; these numbers are fixture wiring checks.");

        return ExitCodes.Success;
    }

    private static async Task<int> WarmupCommandAsync(
        BenchmarkInvocation invocation,
        BenchmarkServices services,
        CancellationToken cancellationToken)
    {
        var (manifest, _) = LoadValidated(services);
        var model = invocation.Model ?? manifest.DefaultCandidate.Model;
        var baseUrl = invocation.BaseUrl ?? manifest.DefaultCandidate.BaseUrl;
        var timeout = invocation.TimeoutSeconds ?? manifest.DefaultCandidate.TimeoutSeconds;
        var gateway = services.CreateGateway(model, baseUrl, timeout);
        var health = await CheckModelAsync(gateway, model, baseUrl, cancellationToken).ConfigureAwait(false);
        var analyzer = CreateAnalyzer(services, model, manifest, gateway);

        services.Output.WriteLine($"ollama {health.Version ?? "unknown"} at {baseUrl} · model {model}");

        var warmupInputs = new[] { "السلام عليكم", "عايز شاشة 24 فيها HDMI" };

        for (var index = 0; index < warmupInputs.Length; index++)
        {
            var testCase = new BenchmarkCase
            {
                Id = $"warmup-{index + 1}",
                Input = warmupInputs[index],
                Expected = new NluOutput { Intent = "ProductSearch" },
                Gating = false,
            };

            var execution = await analyzer.AnalyzeAsync(testCase, cancellationToken).ConfigureAwait(false);

            services.Output.WriteLine(
                $"warm-up {index + 1}: {BenchmarkEvaluator.FormatSeconds(execution.FinalAttemptMilliseconds)} s · "
                + $"schema {(execution.SchemaValid ? "valid" : "invalid")}");
        }

        services.Output.WriteLine("Model is warm. Warm-up latency is never part of the benchmark metrics.");
        return ExitCodes.Success;
    }

    private static async Task<int> RunCommandAsync(
        BenchmarkInvocation invocation,
        BenchmarkServices services,
        CancellationToken cancellationToken)
    {
        var (manifest, dataset) = LoadValidated(services);
        var model = invocation.Model ?? manifest.DefaultCandidate.Model;
        var baseUrl = invocation.BaseUrl ?? manifest.DefaultCandidate.BaseUrl;
        var timeout = invocation.TimeoutSeconds ?? manifest.DefaultCandidate.TimeoutSeconds;
        var runId = invocation.RunId ?? throw new UsageException("The run command requires --run-id <id>.");
        var gateway = services.CreateGateway(model, baseUrl, timeout);
        var health = await CheckModelAsync(gateway, model, baseUrl, cancellationToken).ConfigureAwait(false);
        var artifact = await ExecuteRunAsync(
            services,
            manifest,
            dataset,
            gateway,
            runId,
            RunModes.Live,
            model,
            baseUrl,
            health,
            cancellationToken).ConfigureAwait(false);

        var store = new RunArtifactStore(services.Paths.ResultsDirectory);
        store.Save(artifact);

        if (BenchmarkRunner.IsInfrastructureFailure(artifact))
        {
            throw new OllamaTransportException(
                $"Every case failed on transport; {store.PathFor(runId)} was kept for diagnosis but no report was produced.");
        }

        var evaluation = BenchmarkEvaluator.Evaluate(manifest, dataset, [artifact]);

        services.Output.WriteLine();
        services.Output.WriteLine($"run artifact: {store.PathFor(runId)}");
        PrintEvaluation(services, evaluation);
        services.Output.WriteLine("Run the second pass, then 'report --runs <run1>,<run2>' for the final artifact.");

        return ExitCodes.Success;
    }

    private static int ReportCommand(BenchmarkInvocation invocation, BenchmarkServices services)
    {
        var (manifest, dataset) = LoadValidated(services);
        var store = new RunArtifactStore(services.Paths.ResultsDirectory);
        var runs = invocation.Runs.Select(store.Load).ToArray();

        foreach (var run in runs)
        {
            if (!string.Equals(run.Mode, RunModes.Live, StringComparison.Ordinal))
            {
                throw new BenchmarkDataException(
                    $"Run {run.RunId} has mode '{run.Mode}'. Final reports require live runs; "
                    + "dry-run artifacts are never benchmark evidence.");
            }
        }

        if (runs.Select(run => run.Model).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new BenchmarkDataException("Both runs must measure the same model tag.");
        }

        if (runs.Select(run => run.DatasetSha256).Distinct(StringComparer.Ordinal).Count() != 1
            || runs.Select(run => run.SchemaSha256).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new BenchmarkDataException("Both runs must use the same dataset and schema hashes.");
        }

        if (runs.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() != runs.Length)
        {
            throw new BenchmarkDataException("Each run id must be distinct.");
        }

        var evaluation = BenchmarkEvaluator.Evaluate(manifest, dataset, runs);
        var outputs = BenchmarkReportWriter.Write(
            services.Paths.ReportsDirectory,
            runs,
            evaluation,
            manifest,
            dataset,
            services.Clock);

        services.Output.WriteLine($"model: {runs[0].Model}");
        PrintEvaluation(services, evaluation);
        services.Output.WriteLine($"report: {outputs.MarkdownPath}");
        services.Output.WriteLine($"report data: {outputs.JsonPath}");

        return ExitCodes.Success;
    }

    private static async Task<RunArtifact> ExecuteRunAsync(
        BenchmarkServices services,
        BenchmarkManifest manifest,
        BenchmarkDataset dataset,
        IOllamaGateway gateway,
        string runId,
        string mode,
        string model,
        string baseUrl,
        OllamaHealth health,
        CancellationToken cancellationToken)
    {
        var analyzer = CreateAnalyzer(services, model, manifest, gateway);
        var environment = services.EnvironmentCollector.Collect(health.Version);
        var context = new RunContext
        {
            RunId = runId,
            Mode = mode,
            Model = model,
            BaseUrl = baseUrl,
            Settings = new RunSettings
            {
                Temperature = manifest.DefaultCandidate.Temperature,
                ContextTokens = manifest.DefaultCandidate.ContextTokens,
                TimeoutSeconds = manifest.DefaultCandidate.TimeoutSeconds,
                RetryPolicy = manifest.DefaultCandidate.RetryPolicy,
            },
            Environment = environment,
        };

        services.Output.WriteLine(
            mode == RunModes.DryRun
                ? $"dry-run {runId}: {dataset.Cases.Count} cases against the offline fixture"
                : $"run {runId}: {dataset.Cases.Count} cases against {model}");

        var runner = new BenchmarkRunner(analyzer);

        return await runner.RunAsync(
            dataset,
            manifest,
            context,
            progress => PrintProgress(services, progress),
            cancellationToken).ConfigureAwait(false);
    }

    private static NluAnalyzer CreateAnalyzer(
        BenchmarkServices services,
        string model,
        BenchmarkManifest manifest,
        IOllamaGateway gateway)
    {
        var builder = NluRequestBuilder.FromFile(services.Paths.SchemaFile);

        return new NluAnalyzer(
            gateway,
            builder,
            JsonSchemaValidator.FromFile(services.Paths.SchemaFile),
            new NluRequestParameters
            {
                Model = model,
                Temperature = manifest.DefaultCandidate.Temperature,
                ContextTokens = manifest.DefaultCandidate.ContextTokens,
            });
    }

    private static async Task<OllamaHealth> CheckModelAsync(
        IOllamaGateway gateway,
        string model,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        OllamaHealth health;

        try
        {
            health = await gateway.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OllamaTransportException exception)
        {
            throw new OllamaTransportException(
                $"{exception.Message} Start Ollama (ollama serve) and confirm {baseUrl}.",
                exception);
        }

        if (!health.Models.Contains(model, StringComparer.Ordinal))
        {
            throw new OllamaTransportException(
                $"Model '{model}' is not available in Ollama at {baseUrl}. "
                + $"Available: {(health.Models.Count == 0 ? "none" : string.Join(", ", health.Models))}. "
                + $"Download it deliberately with: ollama pull {model}");
        }

        return health;
    }

    private static (BenchmarkManifest Manifest, BenchmarkDataset Dataset) LoadValidated(
        BenchmarkServices services)
    {
        var manifest = BenchmarkManifest.Load(services.Paths);
        var manifestErrors = manifest.Validate();

        if (manifestErrors.Count > 0)
        {
            throw new BenchmarkDataException($"manifest problems: {string.Join("; ", manifestErrors)}");
        }

        var dataset = BenchmarkDataset.Load(services.Paths, manifest);
        var datasetErrors = dataset.Validate(manifest);

        if (datasetErrors.Count > 0)
        {
            throw new BenchmarkDataException($"dataset problems: {string.Join("; ", datasetErrors)}");
        }

        var schemaHash = Hashing.Sha256File(services.Paths.SchemaFile);

        if (!string.Equals(schemaHash, manifest.Schema.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BenchmarkDataException(
                $"Schema hash {schemaHash} does not match manifest hash {manifest.Schema.Sha256}.");
        }

        var validator = JsonSchemaValidator.FromFile(services.Paths.SchemaFile);
        var invalidExpectations = new List<string>();

        foreach (var testCase in dataset.Cases)
        {
            var errors = validator.Validate(JsonSerializer.Serialize(testCase.Expected, BenchmarkJson.Options));

            if (errors.Count > 0)
            {
                invalidExpectations.Add($"case '{testCase.Id}': {errors[0]}");
            }
        }

        if (invalidExpectations.Count > 0)
        {
            throw new BenchmarkDataException(
                $"expected outputs must satisfy the benchmark schema: {string.Join("; ", invalidExpectations)}");
        }

        return (manifest, dataset);
    }

    private static void PrintProgress(BenchmarkServices services, RunProgress progress)
    {
        var state = progress.SchemaValid ? "ok" : "FAILED";

        if (progress.Retried)
        {
            state += " (retried)";
        }

        services.Output.WriteLine(
            $"[{progress.Index.ToString(CultureInfo.InvariantCulture)}/{progress.Total.ToString(CultureInfo.InvariantCulture)}] "
            + $"{progress.CaseId} {state} {BenchmarkEvaluator.FormatSeconds(progress.Milliseconds)} s");
    }

    private static void PrintEvaluation(BenchmarkServices services, BenchmarkEvaluation evaluation)
    {
        services.Output.WriteLine(
            $"cases {evaluation.Metrics.CaseCount} · schema-valid {BenchmarkEvaluator.Format(evaluation.Metrics.SchemaValidPercent)}% · "
            + $"intent {BenchmarkEvaluator.Format(evaluation.Metrics.IntentAccuracyPercent)}% · "
            + $"hard budget {BenchmarkEvaluator.Format(evaluation.Metrics.HardBudgetAccuracyPercent)}% · "
            + $"median {BenchmarkEvaluator.FormatSeconds(evaluation.Metrics.WarmMedianMilliseconds)} s · "
            + $"p95 {BenchmarkEvaluator.FormatSeconds(evaluation.Metrics.WarmP95Milliseconds)} s");

        foreach (var gate in evaluation.Gates)
        {
            services.Output.WriteLine(
                $"  [{(gate.Passed ? "PASS" : "FAIL")}] {gate.Name}: {gate.Measured} (requires {gate.Threshold})");
        }

        services.Output.WriteLine($"overall: {(evaluation.OverallPass ? "PASS" : "FAIL")}");
        services.Output.WriteLine($"decision: {evaluation.Decision.Outcome} — {evaluation.Decision.Recommendation}");
    }
}
