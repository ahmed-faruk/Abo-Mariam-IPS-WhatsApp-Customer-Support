using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>The CLI surface the Intel-Mac runbook depends on.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void Validate_and_dry_run_need_no_options()
    {
        Assert.Equal(BenchmarkCommandKind.Validate, CommandLine.Parse(["validate"]).Command);
        Assert.Equal(BenchmarkCommandKind.DryRun, CommandLine.Parse(["dry-run"]).Command);
        Assert.Equal(BenchmarkCommandKind.Help, CommandLine.Parse(["help"]).Command);
    }

    [Fact]
    public void Warmup_accepts_model_base_url_and_timeout()
    {
        var invocation = CommandLine.Parse(
            ["warmup", "--model", "qwen3.5:2b-q4_K_M", "--base-url", "http://127.0.0.1:11434", "--timeout-seconds", "30"]);

        Assert.Equal(BenchmarkCommandKind.Warmup, invocation.Command);
        Assert.Equal("qwen3.5:2b-q4_K_M", invocation.Model);
        Assert.Equal("http://127.0.0.1:11434", invocation.BaseUrl);
        Assert.Equal(30, invocation.TimeoutSeconds);
    }

    [Fact]
    public void Run_requires_a_run_id_and_accepts_inline_values()
    {
        var invocation = CommandLine.Parse(["run", "--model=qwen3:1.7b", "--run-id=run1"]);

        Assert.Equal(BenchmarkCommandKind.Run, invocation.Command);
        Assert.Equal("qwen3:1.7b", invocation.Model);
        Assert.Equal("run1", invocation.RunId);
    }

    [Fact]
    public void Report_parses_a_comma_separated_run_list()
    {
        var invocation = CommandLine.Parse(["report", "--runs", "run1, run2"]);

        Assert.Equal(BenchmarkCommandKind.Report, invocation.Command);
        Assert.Equal(["run1", "run2"], invocation.Runs);
    }

    [Theory]
    [InlineData(new string[] { }, "No command was given")]
    [InlineData(new[] { "benchmark" }, "Unknown command")]
    [InlineData(new[] { "validate", "--wat" }, "Unknown option")]
    [InlineData(new[] { "warmup", "--model" }, "requires a value")]
    [InlineData(new[] { "warmup", "--timeout-seconds", "0" }, "positive integer")]
    [InlineData(new[] { "warmup", "--timeout-seconds", "soon" }, "positive integer")]
    [InlineData(new[] { "run" }, "requires --run-id")]
    [InlineData(new[] { "run", "--run-id", ".." }, "letters, digits")]
    [InlineData(new[] { "report", "--runs", "run1" }, "requires --runs")]
    public void Invalid_invocations_are_rejected_with_a_clear_message(string[] args, string expectedMessage)
    {
        var exception = Assert.Throws<UsageException>(() => CommandLine.Parse(args));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }
}
