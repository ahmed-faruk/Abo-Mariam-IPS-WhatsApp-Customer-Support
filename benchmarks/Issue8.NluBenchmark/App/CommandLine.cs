using System.Globalization;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

public enum BenchmarkCommandKind
{
    Validate,
    DryRun,
    Warmup,
    Run,
    Report,
    Help,
}

public sealed record BenchmarkInvocation
{
    public required BenchmarkCommandKind Command { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int? TimeoutSeconds { get; init; }

    public string? RunId { get; init; }

    public string[] Runs { get; init; } = [];
}

public static class CommandLine
{
    public const string Usage =
        """
        Issue #8 structured-NLU benchmark (live runs happen on the Intel Mac).

          dotnet run --project benchmarks/Issue8.NluBenchmark -- validate
          dotnet run --project benchmarks/Issue8.NluBenchmark -- dry-run
          dotnet run --project benchmarks/Issue8.NluBenchmark -- warmup   [--model <tag>] [--base-url <url>] [--timeout-seconds <n>]
          dotnet run --project benchmarks/Issue8.NluBenchmark -- run      [--model <tag>] --run-id <id> [--base-url <url>] [--timeout-seconds <n>]
          dotnet run --project benchmarks/Issue8.NluBenchmark -- report    --runs <run1,run2>

        validate     checks the committed dataset, schema and manifest without any network call.
        dry-run      runs the whole pipeline against the offline fixture; never benchmark evidence.
        warmup       verifies Ollama and the model tag, then sends two warm-up requests.
        run          runs the dataset once and stores results/<run-id>.json.
        report       combines two live run artifacts into reports/<model>-intel-mac.{md,json}.

        A model quality gate failure is report data: the process still exits 0. Usage errors exit 1,
        invalid benchmark data exits 2, and an Ollama/infrastructure failure exits 3.
        """;

    public static BenchmarkInvocation Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException("No command was given.");
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "validate" => BenchmarkCommandKind.Validate,
            "dry-run" => BenchmarkCommandKind.DryRun,
            "warmup" => BenchmarkCommandKind.Warmup,
            "run" => BenchmarkCommandKind.Run,
            "report" => BenchmarkCommandKind.Report,
            "help" or "--help" or "-h" => BenchmarkCommandKind.Help,
            _ => throw new UsageException($"Unknown command '{args[0]}'."),
        };

        var invocation = new BenchmarkInvocation { Command = command };

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = SplitOption(argument);

            invocation = name switch
            {
                "--model" => invocation with { Model = Value(args, ref index, name, inlineValue) },
                "--base-url" => invocation with { BaseUrl = Value(args, ref index, name, inlineValue) },
                "--timeout-seconds" => invocation with { TimeoutSeconds = PositiveInt(name, Value(args, ref index, name, inlineValue)) },
                "--run-id" => invocation with { RunId = Identifier(name, Value(args, ref index, name, inlineValue)) },
                "--runs" => invocation with { Runs = RunIds(Value(args, ref index, name, inlineValue)) },
                _ => throw new UsageException($"Unknown option '{argument}'."),
            };
        }

        if (invocation.Command == BenchmarkCommandKind.Run && string.IsNullOrWhiteSpace(invocation.RunId))
        {
            throw new UsageException("The run command requires --run-id <id> so run artifacts are never overwritten by accident.");
        }

        if (invocation.Command == BenchmarkCommandKind.Report && invocation.Runs.Length != 2)
        {
            throw new UsageException(
                "The report command requires --runs <run1,run2> with exactly two measured passes "
                + "(Issue #8: two runs after warm-up, no more and no fewer).");
        }

        if (invocation.Command == BenchmarkCommandKind.Report
            && invocation.Runs.Distinct(StringComparer.Ordinal).Count() != invocation.Runs.Length)
        {
            throw new UsageException("The report command requires two distinct run ids; --runs repeated one.");
        }

        return invocation;
    }

    private static (string Name, string? InlineValue) SplitOption(string argument)
    {
        var separator = argument.IndexOf('=', StringComparison.Ordinal);

        if (separator < 0)
        {
            return (argument, null);
        }

        return (argument[..separator], argument[(separator + 1)..]);
    }

    private static string Value(string[] args, ref int index, string name, string? inlineValue)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new UsageException($"Option {name} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int PositiveInt(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new UsageException($"Option {name} requires a positive integer, got '{value}'.");

    private static string Identifier(string name, string value) =>
        value.Length > 0
        && !value.Contains("..", StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value
            : throw new UsageException($"Option {name} requires letters, digits, '-', '_' or '.', got '{value}'.");

    private static string[] RunIds(string value)
    {
        var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (ids.Length == 0)
        {
            throw new UsageException("Option --runs requires a comma-separated list of run ids.");
        }

        return [.. ids.Select(id => Identifier("--runs", id))];
    }
}
