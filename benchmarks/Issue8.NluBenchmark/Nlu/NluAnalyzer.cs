using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>One request the harness made, and everything observable about it.</summary>
public sealed record NluAttempt
{
    public required bool Correction { get; init; }

    public required string RawContent { get; init; }

    public required long WallClockMilliseconds { get; init; }

    public NluTransportTiming? Timing { get; init; }

    public required bool SchemaValid { get; init; }

    public string[] SchemaErrors { get; init; } = [];

    public string? TransportFailure { get; init; }

    public NluOutput? Output { get; init; }
}

/// <summary>
/// The outcome of one benchmark case: at most two attempts, because docs/TECHNICAL.md
/// section 8.3 allows exactly one retry after invalid structured output. A semantic
/// mismatch never triggers a retry, and neither does a transport failure: only an invalid
/// or unparsable reply from an actual model response does.
/// </summary>
public sealed record NluCaseExecution
{
    public required string CaseId { get; init; }

    public required NluAttempt[] Attempts { get; init; }

    public required bool SchemaValid { get; init; }

    public NluOutput? Output { get; init; }

    public string? FailureReason { get; init; }

    public bool Retried => Attempts.Length > 1;

    /// <summary>Wall clock of the attempt that produced the scored outcome.</summary>
    public long FinalAttemptMilliseconds => Attempts[^1].WallClockMilliseconds;

    /// <summary>Wall clock of the whole case, retry included.</summary>
    public long TotalMilliseconds => Attempts.Sum(attempt => attempt.WallClockMilliseconds);
}

public sealed class NluAnalyzer
{
    private readonly INluTransport _transport;
    private readonly NluRequestBuilder _requestBuilder;
    private readonly JsonSchemaValidator _schemaValidator;
    private readonly NluRequestParameters _parameters;
    private readonly TimeProvider _timeProvider;

    public NluAnalyzer(
        INluTransport transport,
        NluRequestBuilder requestBuilder,
        JsonSchemaValidator schemaValidator,
        NluRequestParameters parameters,
        TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _requestBuilder = requestBuilder;
        _schemaValidator = schemaValidator;
        _parameters = parameters;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<NluCaseExecution> AnalyzeAsync(
        BenchmarkCase testCase,
        CancellationToken cancellationToken)
    {
        var first = await SendAsync(testCase.Input, correctionErrors: null, cancellationToken).ConfigureAwait(false);

        if (first.SchemaValid)
        {
            return new NluCaseExecution
            {
                CaseId = testCase.Id,
                Attempts = [first],
                SchemaValid = true,
                Output = first.Output,
            };
        }

        // A transport failure produced no model reply at all, so it is infrastructure evidence.
        // Retrying it would let an outage disappear behind a later response, and the documented
        // retry exists only for invalid structured output from an actual reply.
        if (first.TransportFailure is not null)
        {
            return new NluCaseExecution
            {
                CaseId = testCase.Id,
                Attempts = [first],
                SchemaValid = false,
                FailureReason = $"transport-failure: {first.TransportFailure}",
            };
        }

        var retry = await SendAsync(testCase.Input, first.SchemaErrors, cancellationToken).ConfigureAwait(false);

        return new NluCaseExecution
        {
            CaseId = testCase.Id,
            Attempts = [first, retry],
            SchemaValid = retry.SchemaValid,
            Output = retry.Output,
            FailureReason = retry.SchemaValid ? null : DescribeFailure(first, retry),
        };
    }

    private async Task<NluAttempt> SendAsync(
        string userInput,
        IReadOnlyList<string>? correctionErrors,
        CancellationToken cancellationToken)
    {
        var request = new NluTransportRequest
        {
            Body = _requestBuilder.Build(_parameters, userInput, correctionErrors),
            UserInput = userInput,
            IsCorrection = correctionErrors is { Count: > 0 },
        };

        var startedAt = _timeProvider.GetTimestamp();
        NluTransportResponse response;

        try
        {
            response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OllamaTransportException exception)
        {
            return new NluAttempt
            {
                Correction = request.IsCorrection,
                RawContent = string.Empty,
                WallClockMilliseconds = ElapsedMilliseconds(startedAt),
                SchemaValid = false,
                TransportFailure = exception.Message,
            };
        }

        var errors = _schemaValidator.Validate(response.Content);
        NluOutput? output = null;

        if (errors.Count == 0)
        {
            try
            {
                output = BenchmarkJson.Deserialize<NluOutput>(response.Content);
            }
            catch (Exception exception) when (exception is JsonException or BenchmarkDataException)
            {
                errors = [$"$: schema-valid payload could not be read as the documented contract ({exception.Message})"];
            }
        }

        return new NluAttempt
        {
            Correction = request.IsCorrection,
            RawContent = response.Content,
            WallClockMilliseconds = ElapsedMilliseconds(startedAt),
            Timing = response.Timing,
            SchemaValid = errors.Count == 0,
            SchemaErrors = [.. errors],
            Output = errors.Count == 0 ? output : null,
        };
    }

    private long ElapsedMilliseconds(long startedAt) =>
        (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

    private static string DescribeFailure(NluAttempt first, NluAttempt retry)
    {
        if (retry.TransportFailure is not null)
        {
            return $"transport-failure: {retry.TransportFailure}";
        }

        var errors = retry.SchemaErrors.Length > 0 ? retry.SchemaErrors : first.SchemaErrors;
        return errors.Length == 0
            ? "schema-failure: reply did not match the documented contract"
            : $"schema-failure: {errors[0]}";
    }
}
