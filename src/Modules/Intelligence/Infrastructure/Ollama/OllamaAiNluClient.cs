using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The production <see cref="IAiNluClient"/>. It owns the documented retry policy of
/// docs/TECHNICAL.md section 8.3: a reply that arrived but failed validation earns exactly one
/// corrective retry; a caller cancellation propagates; and a timeout or an unreachable runtime is
/// never corrected, because no model reply exists to correct.
/// </summary>
public sealed class OllamaAiNluClient : IAiNluClient
{
    private readonly IOllamaChatTransport _transport;
    private readonly OllamaChatRequestBuilder _requestBuilder;

    public OllamaAiNluClient(IOllamaChatTransport transport, OllamaChatRequestBuilder requestBuilder)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(requestBuilder);

        _transport = transport;
        _requestBuilder = requestBuilder;
    }

    public async Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(context);

        context.EnsureWithinBounds();

        // Issue #10 accepts the bounded context but does not serialize it: the frozen prompt v3 of
        // docs/TECHNICAL.md section 8.2 declares that the task sends a single text turn, so injecting
        // history would invalidate the measured prompt identity. Issue #11 owns resolving references
        // over that context with a frozen-compatible mechanism.
        var first = await AttemptAsync(message, correctionProblems: null, cancellationToken).ConfigureAwait(false);

        if (first.Failure is { } firstFailure)
        {
            return firstFailure;
        }

        if (first.Validation is { IsValid: true, Interpretation: { } interpretation })
        {
            return NluAnalysisResult.Success(NluDeterministicNormalizer.Normalize(message, interpretation));
        }

        var retry = await AttemptAsync(
            message,
            first.Validation?.Problems ?? [],
            cancellationToken).ConfigureAwait(false);

        if (retry.Failure is { } retryFailure)
        {
            return retryFailure;
        }

        return retry.Validation is { IsValid: true, Interpretation: { } retryInterpretation }
            ? NluAnalysisResult.Success(NluDeterministicNormalizer.Normalize(message, retryInterpretation))
            : NluAnalysisResult.InvalidModelOutput(retry.Validation?.Problems ?? []);
    }

    private async Task<Attempt> AttemptAsync(
        string message,
        IReadOnlyList<string>? correctionProblems,
        CancellationToken cancellationToken)
    {
        var request = _requestBuilder.Build(message, correctionProblems);
        var transport = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);

        return transport.Outcome switch
        {
            OllamaChatOutcome.ReplyReceived => new Attempt
            {
                Validation = NluReplyValidator.Validate(transport.Content ?? string.Empty),
            },
            OllamaChatOutcome.TimedOut => new Attempt { Failure = NluAnalysisResult.TimedOut() },
            _ => new Attempt { Failure = NluAnalysisResult.AiUnavailable() },
        };
    }

    private sealed record Attempt
    {
        public NluReplyValidation? Validation { get; init; }

        public NluAnalysisResult? Failure { get; init; }
    }
}
