using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>Options that shape one Ollama chat request.</summary>
public sealed record NluRequestParameters
{
    public required string Model { get; init; }

    public required int Temperature { get; init; }

    public required int ContextTokens { get; init; }
}

/// <summary>
/// One structured-NLU request. <see cref="UserInput"/> and <see cref="IsCorrection"/> are
/// harness metadata for fixtures; only <see cref="Body"/> is sent to Ollama.
/// </summary>
public sealed record NluTransportRequest
{
    public required JsonObject Body { get; init; }

    public required string UserInput { get; init; }

    public required bool IsCorrection { get; init; }
}

/// <summary>Ollama timing metadata, in the units Ollama reports (nanoseconds for durations).</summary>
public sealed record NluTransportTiming
{
    public long? TotalDurationNanoseconds { get; init; }

    public long? LoadDurationNanoseconds { get; init; }

    public int? PromptEvalCount { get; init; }

    public long? PromptEvalDurationNanoseconds { get; init; }

    public int? EvalCount { get; init; }

    public long? EvalDurationNanoseconds { get; init; }
}

public sealed record NluTransportResponse
{
    public required string Content { get; init; }

    public NluTransportTiming? Timing { get; init; }
}

/// <summary>The seam the harness sends through: real Ollama in a live run, fixtures offline.</summary>
public interface INluTransport
{
    Task<NluTransportResponse> SendAsync(NluTransportRequest request, CancellationToken cancellationToken);
}
