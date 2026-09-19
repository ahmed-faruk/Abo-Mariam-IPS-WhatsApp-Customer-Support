using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// Issue #8 keeps two raw measured passes as evidence, so a measured artifact is written with
/// create-new semantics: neither a command pre-check nor a direct caller can replace history.
/// Only the explicitly disposable dry-run location may be rewritten.
/// </summary>
public sealed class RunArtifactStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"issue8-store-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Save_writes_and_Load_reads_the_artifact()
    {
        var store = new RunArtifactStore(_directory);
        var artifact = BenchmarkFixtures.CompleteRun("run1");

        Assert.False(store.Exists("run1"));

        store.Save(artifact);

        Assert.True(store.Exists("run1"));
        Assert.True(File.Exists(store.PathFor("run1")));

        var loaded = store.Load("run1");

        Assert.Equal("run1", loaded.RunId);
        Assert.Equal(artifact.Cases.Length, loaded.Cases.Length);
    }

    [Fact]
    public void Save_cannot_overwrite_existing_raw_evidence()
    {
        var store = new RunArtifactStore(_directory);
        var artifact = BenchmarkFixtures.CompleteRun("run1");
        store.Save(artifact);
        var original = File.ReadAllBytes(store.PathFor("run1"));

        var exception = Assert.Throws<BenchmarkDataException>(
            () => store.Save(artifact with { Model = "a-different-model" }));

        Assert.Contains("never overwrites", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(store.PathFor("run1")));
        Assert.Equal("test-model", store.Load("run1").Model);
    }

    [Fact]
    public void Load_reports_a_missing_artifact_clearly()
    {
        var store = new RunArtifactStore(_directory);

        var exception = Assert.Throws<BenchmarkDataException>(() => store.Load("run9"));

        Assert.Contains("run9.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dry_run_location_is_explicitly_rewritable()
    {
        var store = new RunArtifactStore(Path.Combine(_directory, "dry-run"));
        var artifact = BenchmarkFixtures.CompleteRun("dry-run-1") with { Mode = RunModes.DryRun };

        store.SaveSynthetic(artifact);
        store.SaveSynthetic(artifact with { Model = "another-fixture" });

        Assert.Equal("another-fixture", store.Load("dry-run-1").Model);
    }
}
