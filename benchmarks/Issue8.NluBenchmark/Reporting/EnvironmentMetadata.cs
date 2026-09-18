using System.Diagnostics;
using System.Globalization;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Host facts required to reproduce a latency measurement. Captured locally with normal
/// macOS commands; nothing here changes machine configuration.
/// </summary>
public sealed record EnvironmentMetadata
{
    public required string OsVersion { get; init; }

    public required string Architecture { get; init; }

    public required string Cpu { get; init; }

    public long? MemoryBytes { get; init; }

    public string? OllamaVersion { get; init; }

    public required string CollectedAtUtc { get; init; }
}

public sealed class EnvironmentMetadataCollector
{
    private readonly Func<string, string?> _runCommand;
    private readonly Func<DateTimeOffset> _clock;

    public EnvironmentMetadataCollector(Func<string, string?>? runCommand = null, Func<DateTimeOffset>? clock = null)
    {
        _runCommand = runCommand ?? RunShellCommand;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public EnvironmentMetadata Collect(string? ollamaVersion) => new()
    {
        OsVersion = _runCommand("sw_vers -productVersion") ?? "unknown",
        Architecture = _runCommand("uname -m") ?? "unknown",
        Cpu = _runCommand("sysctl -n machdep.cpu.brand_string") ?? "unknown",
        MemoryBytes = ParseLong(_runCommand("sysctl -n hw.memsize")),
        OllamaVersion = ollamaVersion,
        CollectedAtUtc = _clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    };

    public static string DescribeMemory(long? bytes) =>
        bytes is null or <= 0
            ? "unknown"
            : (bytes.Value / 1024.0 / 1024.0 / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " GB";

    private static long? ParseLong(string? value) =>
        long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? RunShellCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = parts[0],
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            for (var index = 1; index < parts.Length; index++)
            {
                process.StartInfo.ArgumentList.Add(parts[index]);
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);

            return process.HasExited && process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
