using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// Offline end-to-end command behaviour: the whole pipeline without Ollama, plus the exit-code
/// contract (0 completed, 1 usage, 2 invalid data, 3 infrastructure).
/// </summary>
public sealed class BenchmarkEntryPointTests
{
    private const string MinimalValidOutput = FixtureNluGateway.MinimalValidOutput;

    [Fact]
    public async Task Validate_checks_the_committed_inputs_without_any_network_call()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out var output);

            var exitCode = await BenchmarkEntryPoint.RunAsync(["validate"], services, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Contains("60 cases", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("57 gating", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("5 dedicated hard-budget", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dry_run_writes_synthetic_artifacts_and_never_claims_benchmark_evidence()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _);

            var exitCode = await BenchmarkEntryPoint.RunAsync(["dry-run"], services, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);

            var dryRunDirectory = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results", "dry-run");
            var markdown = Directory.GetFiles(dryRunDirectory, "*.md").Single();
            var content = File.ReadAllText(markdown);

            Assert.Contains(BenchmarkReportWriter.DryRunBanner, content, StringComparison.Ordinal);
            Assert.Contains("Overall: **FAIL**", content, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(dryRunDirectory, "*-dry-run.json"));
            Assert.Single(Directory.GetFiles(dryRunDirectory, "dry-run-1.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Report_refuses_dry_run_artifacts()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _);

            await BenchmarkEntryPoint.RunAsync(["dry-run"], services, CancellationToken.None);

            var results = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results");
            Directory.CreateDirectory(results);
            File.Copy(
                Path.Combine(results, "dry-run", "dry-run-1.json"),
                Path.Combine(results, "run1.json"));
            File.Copy(
                Path.Combine(results, "dry-run", "dry-run-1.json"),
                Path.Combine(results, "run2.json"));

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["report", "--runs", "run1,run2"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidBenchmarkData, exitCode);
            Assert.Contains("dry-run artifacts are never benchmark evidence", services.Error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Two_live_runs_produce_the_final_report()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
            };

            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None));
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run2"], services, CancellationToken.None));
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["report", "--runs", "run1,run2"], services, CancellationToken.None));

            var reports = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "reports");
            var markdown = Directory.GetFiles(reports, "*.md").Single();
            var content = File.ReadAllText(markdown);

            Assert.Contains("| run1 |", content, StringComparison.Ordinal);
            Assert.Contains("| run2 |", content, StringComparison.Ordinal);
            Assert.DoesNotContain(BenchmarkReportWriter.DryRunBanner, content, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(reports, "*.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Report_rejects_runs_measured_on_a_different_dataset()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
            };

            await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None);
            await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run2"], services, CancellationToken.None);

            var artifactPath = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results", "run2.json");
            var tampered = File.ReadAllText(artifactPath).Replace(
                BenchmarkFixtures.Dataset.Sha256,
                new string('1', 64),
                StringComparison.Ordinal);
            File.WriteAllText(artifactPath, tampered);

            var exitCode = await BenchmarkEntryPoint.RunAsync(["report", "--runs", "run1,run2"], services, CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidBenchmarkData, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_unavailable_model_is_an_infrastructure_failure_with_clear_instructions()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new MissingModelGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["run", "--run-id", "run1", "--model", "qwen3.5:2b-q4_K_M"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.InfrastructureFailure, exitCode);
            Assert.Contains("ollama pull qwen3.5:2b-q4_K_M", services.Error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_run_where_every_case_fails_on_transport_exits_three_and_keeps_the_artifact()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new FailingTransportGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None);

            Assert.Equal(ExitCodes.InfrastructureFailure, exitCode);
            Assert.True(File.Exists(Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results", "run1.json")));
            Assert.Contains("no report was produced", services.Error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_partial_ollama_outage_invalidates_the_run_and_keeps_it_out_of_the_report()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var outage = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new OutageAfterFirstCaseGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], outage, CancellationToken.None);

            // One case answered, then Ollama went away: that is infrastructure evidence, not a
            // poor-model quality result, so the run exits 3 instead of being scored.
            Assert.Equal(ExitCodes.InfrastructureFailure, exitCode);

            var results = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results");
            Assert.True(File.Exists(Path.Combine(results, "run1.json")));
            Assert.Contains("no report was produced", outage.Error.ToString(), StringComparison.Ordinal);

            var healthy = outage with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
            };
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run2"], healthy, CancellationToken.None));

            var reportExitCode = await BenchmarkEntryPoint.RunAsync(
                ["report", "--runs", "run1,run2"],
                healthy,
                CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidBenchmarkData, reportExitCode);
            Assert.Contains("infrastructure-failure evidence", healthy.Error.ToString(), StringComparison.Ordinal);

            var reports = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "reports");

            Assert.Empty(Directory.Exists(reports) ? Directory.GetFiles(reports, "*.md") : []);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_timeout_override_is_used_for_transport_and_persisted_in_the_artifact()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var transportTimeout = 0;
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, timeoutSeconds) =>
                {
                    transportTimeout = timeoutSeconds;

                    return new ValidOnlyGateway();
                },
            };

            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(
                    ["run", "--run-id", "run1", "--timeout-seconds", "45"],
                    services,
                    CancellationToken.None));
            Assert.Equal(45, transportTimeout);

            var results = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results");
            var artifact = BenchmarkJson.Deserialize<RunArtifact>(
                File.ReadAllText(Path.Combine(results, "run1.json")));

            Assert.Equal(45, artifact.Settings.TimeoutSeconds);

            await BenchmarkEntryPoint.RunAsync(
                ["run", "--run-id", "run2", "--timeout-seconds", "45"],
                services,
                CancellationToken.None);
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["report", "--runs", "run1,run2"], services, CancellationToken.None));

            var reports = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "reports");
            var reportJson = Directory.GetFiles(reports, "*.json").Single();
            var report = BenchmarkJson.Deserialize<BenchmarkReport>(File.ReadAllText(reportJson));

            Assert.Equal(45, report.Settings.TimeoutSeconds);
            Assert.Contains("45 s", File.ReadAllText(Path.ChangeExtension(reportJson, ".md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_existing_run_id_is_rejected_and_the_original_evidence_is_untouched()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
            };

            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None));

            var artifactPath = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results", "run1.json");
            var original = File.ReadAllBytes(artifactPath);

            var exitCode = await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None);

            Assert.Equal(ExitCodes.InvalidBenchmarkData, exitCode);
            Assert.Contains("never overwrites", services.Error.ToString(), StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(artifactPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Commands_persist_and_load_through_the_configured_artifact_store()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var alternative = Path.Combine(root, "alternative-results");
            var services = BenchmarkFixtures.CreateServices(root, out _)
                with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
                CreateArtifactStore = _ => new RunArtifactStore(alternative),
            };

            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run1"], services, CancellationToken.None));
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["run", "--run-id", "run2"], services, CancellationToken.None));

            var defaultResults = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results");

            Assert.True(File.Exists(Path.Combine(alternative, "run1.json")));
            Assert.True(File.Exists(Path.Combine(alternative, "run2.json")));
            Assert.False(Directory.Exists(defaultResults));

            // The report command loads through the same collaborator.
            Assert.Equal(
                ExitCodes.Success,
                await BenchmarkEntryPoint.RunAsync(["report", "--runs", "run1,run2"], services, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Usage_errors_exit_one_and_invalid_benchmark_data_exits_two()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out _);

            Assert.Equal(
                ExitCodes.UsageError,
                await BenchmarkEntryPoint.RunAsync(["nonsense"], services, CancellationToken.None));

            var datasetPath = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "Data", "v1", "cases.jsonl");
            File.WriteAllText(datasetPath, File.ReadAllText(datasetPath).Replace(
                "\"intent\":\"Greeting\"",
                "\"intent\":\"NotAnIntent\"",
                StringComparison.Ordinal));

            Assert.Equal(
                ExitCodes.InvalidBenchmarkData,
                await BenchmarkEntryPoint.RunAsync(["validate"], services, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Warmup_reports_success_only_after_schema_valid_replies_and_writes_no_artifacts()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out var output)
                with
            {
                CreateGateway = (_, _, _) => new ValidOnlyGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["warmup", "--model", "qwen3.5:2b-q4_K_M"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Contains("warm-up 1:", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("warm-up 2:", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Model is warm.", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, services.Error.ToString());
            Assert.False(Directory.Exists(Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "results")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Warmup_that_times_out_fails_without_claiming_the_model_is_warm()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out var output)
                with
            {
                CreateGateway = (_, _, _) => new TimedOutGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["warmup", "--model", "qwen3.5:2b-q4_K_M"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.InfrastructureFailure, exitCode);
            Assert.DoesNotContain("Model is warm.", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("warm-up failed", services.Error.ToString(), StringComparison.Ordinal);
            Assert.Contains("timed out", services.Error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Warmup_that_stays_schema_invalid_fails_without_claiming_the_model_is_warm()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var services = BenchmarkFixtures.CreateServices(root, out var output)
                with
            {
                CreateGateway = (_, _, _) => new InvalidOutputGateway(),
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["warmup", "--model", "qwen3.5:2b-q4_K_M"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.InfrastructureFailure, exitCode);
            Assert.DoesNotContain("Model is warm.", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("not schema-valid", services.Error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Warmup_retries_a_schema_invalid_reply_once_before_succeeding()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var gateway = new ScriptedWarmupGateway(
                FixtureNluGateway.InvalidOutput,
                FixtureNluGateway.MinimalValidOutput,
                FixtureNluGateway.MinimalValidOutput);
            var services = BenchmarkFixtures.CreateServices(root, out var output)
                with
            {
                CreateGateway = (_, _, _) => gateway,
            };

            var exitCode = await BenchmarkEntryPoint.RunAsync(
                ["warmup", "--model", "qwen3.5:2b-q4_K_M"],
                services,
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.Equal(3, gateway.Requests.Count);
            Assert.True(gateway.Requests[1].IsCorrection);
            Assert.Contains("Model is warm.", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TimedOutGateway : IOllamaGateway
    {
        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken) =>
            throw new OllamaTransportException("Ollama did not answer within 20 seconds.");
    }

    private sealed class InvalidOutputGateway : IOllamaGateway
    {
        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new NluTransportResponse { Content = FixtureNluGateway.InvalidOutput });
    }

    private sealed class ScriptedWarmupGateway : IOllamaGateway
    {
        private readonly Queue<string> _responses;

        public ScriptedWarmupGateway(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<NluTransportRequest> Requests { get; } = [];

        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new NluTransportResponse { Content = _responses.Dequeue() });
        }
    }

    private sealed class ValidOnlyGateway : IOllamaGateway
    {
        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new NluTransportResponse { Content = MinimalValidOutput });
    }

    private sealed class MissingModelGateway : IOllamaGateway
    {
        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3:1.7b"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The model check should have failed first.");
    }

    private sealed class FailingTransportGateway : IOllamaGateway
    {
        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken) =>
            throw new OllamaTransportException("scripted transport failure");
    }

    /// <summary>A live Ollama that answers the first case and then disappears mid-run.</summary>
    private sealed class OutageAfterFirstCaseGateway : IOllamaGateway
    {
        private int _calls;

        public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaHealth("test-ollama", ["qwen3.5:2b-q4_K_M"]));

        public Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken)
        {
            _calls++;

            return _calls > 1
                ? throw new OllamaTransportException("Ollama is not reachable at http://127.0.0.1:11434.")
                : Task.FromResult(new NluTransportResponse { Content = MinimalValidOutput });
        }
    }
}
