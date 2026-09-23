using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Meta;

internal sealed class MetaOutboundMessageSender(
    HttpClient httpClient,
    WhatsAppOptions options) : IOutboundMessageSender
{
    /// <summary>
    /// The attempt budget expired, so the application cannot prove whether the provider accepted the
    /// request. It is never reported as a refusal.
    /// </summary>
    internal const string TimeoutDiagnostic = "MetaUnknownTimeout";

    /// <summary>A success response whose provider message id could not be established.</summary>
    internal const string MalformedSuccessDiagnostic = "MetaMalformedSuccessResponse";

    /// <summary>The provider response exceeded <see cref="MaxProviderResponseBytes"/> and was not parsed.</summary>
    internal const string ResponseTooLargeDiagnostic = "MetaResponseTooLarge";

    /// <summary>A transport failure that could have happened after the request left the process.</summary>
    internal const string UnknownTransportDiagnostic = "MetaUnknownTransport";

    /// <summary>
    /// The largest provider response this adapter reads and parses. A Meta response is a small JSON
    /// document, so a body beyond this bound is never buffered in full and is reported as an
    /// unclassified outcome instead of being parsed.
    /// </summary>
    internal const int MaxProviderResponseBytes = 64 * 1024;

    private const int ResponseReadChunkBytes = 8 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Provider error codes that mean "try again later". 131000 and 131057 are documented temporary
    // WhatsApp errors, so they are never treated as permanent merely because they arrive with a 4xx.
    private static readonly HashSet<int> RetryableMetaCodes = [2, 4, 80007, 130429, 131000, 131016, 131056, 131057];
    private static readonly HashSet<int> PermanentMetaCodes = [100, 190, 130403, 131008, 131009, 131047];

    public async Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body.Length > WhatsAppOptions.MaxTextBodyLength)
        {
            return OutboundSendResult.PermanentFailure("MetaPermanentFailure oversized-text-body");
        }

        // One configured attempt budget covers the complete attempt: request transmission, response
        // headers, response streaming and the classification of what came back. The caller's token
        // stays separate so a shutdown is propagated instead of being reported as a provider outcome.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            return await AttemptAsync(message, attempt.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return OutboundSendResult.Unknown(TimeoutDiagnostic);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // A transport failure can happen after the request left the process, so the attempt
            // outcome is unknown. Only the exception category is kept: provider text and exception
            // messages are unbounded and can carry customer data.
            return OutboundSendResult.Unknown(
                string.Create(CultureInfo.InvariantCulture, $"{UnknownTransportDiagnostic} {exception.GetType().Name}"));
        }
    }

    private async Task<OutboundSendResult> AttemptAsync(ClaimedOutboxMessage message, CancellationToken attemptToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.facebook.com/{options.ApiVersion}/{options.PhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreateTextPayload(message), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            attemptToken);

        // The body is read completely within a byte bound and only then parsed: a truncated document
        // is not a document, so a parsed result is never derived from partial JSON.
        var body = await ReadBoundedBodyAsync(response.Content, attemptToken);
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            if (body is null)
            {
                return OutboundSendResult.Unknown(ResponseTooLargeDiagnostic);
            }

            return TryReadProviderMessageId(body, out var providerMessageId)
                ? OutboundSendResult.Sent(providerMessageId)
                : OutboundSendResult.Unknown(MalformedSuccessDiagnostic);
        }

        // A read boundary is not provider evidence, so an unread body is never guessed at.
        var error = body is null ? MetaError.None : MetaError.From(body);
        var fallback = body is null
            ? ResponseTooLargeDiagnostic
            : Diagnostic("MetaUnknownOutcome", status, error.Code);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return OutboundSendResult.RetryableFailure(
                Diagnostic("MetaRetryableFailure", status, error.Code),
                RetryAfter(response));
        }

        if (error.Code is int code)
        {
            if (RetryableMetaCodes.Contains(code))
            {
                return OutboundSendResult.RetryableFailure(
                    Diagnostic("MetaRetryableFailure", status, code),
                    RetryAfter(response));
            }
        }

        // A 5xx never proves that Meta refused the request, whatever the body carries, so acceptance
        // stays uncertain and the attempt is never dead-lettered on the strength of the status class.
        if (status >= 500)
        {
            return OutboundSendResult.Unknown(fallback);
        }

        if (error.Code is int permanentCode && PermanentMetaCodes.Contains(permanentCode))
        {
            return OutboundSendResult.PermanentFailure(
                Diagnostic("MetaPermanentFailure", status, permanentCode));
        }

        // Any other non-success status is a known rejection, but an unrecognised provider code does
        // not prove that the rejection is permanent, so it is retried within the attempt budget
        // instead of being dead-lettered on a guess.
        return OutboundSendResult.RetryableFailure(fallback, RetryAfter(response));
    }

    /// <summary>
    /// Reads the complete provider response, or reports that it is beyond the bound. The content is
    /// streamed under the same attempt budget as the request, so a stalled body can never outlive the
    /// configured attempt timeout.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpContent content, CancellationToken attemptToken)
    {
        await using var stream = await content.ReadAsStreamAsync(attemptToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[ResponseReadChunkBytes];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, attemptToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxProviderResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static object CreateTextPayload(ClaimedOutboxMessage message) =>
        new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = message.CustomerExternalId,
            type = "text",
            text = new
            {
                body = message.Body,
            },
        };

    private static bool TryReadProviderMessageId(byte[] body, out string providerMessageId)
    {
        providerMessageId = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            // A well-formed document of the wrong shape is still an unusable document: the element API
            // throws for a non-object root, so the shape is checked instead of being caught later.
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array
                || messages.GetArrayLength() == 0)
            {
                return false;
            }

            var first = messages[0];

            if (first.ValueKind != JsonValueKind.Object
                || !first.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            providerMessageId = id.GetString() ?? string.Empty;

            return !string.IsNullOrWhiteSpace(providerMessageId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The provider hint is carried only when it is a positive delay. Bounding the durable retry
    /// schedule stays with the queue that owns it, and the adapter never waits on the hint itself.
    /// </summary>
    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var hint = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => (TimeSpan?)null,
        };

        if (hint is not { } delay || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay;
    }

    /// <summary>
    /// Stable, bounded diagnostics: a classification, the HTTP status and the provider code. Provider
    /// free text, response bodies and exception messages are never persisted or logged.
    /// </summary>
    private static string Diagnostic(string classification, int status, int? code)
    {
        var codeText = code is int value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "none";

        return string.Create(CultureInfo.InvariantCulture, $"{classification} status={status} code={codeText}");
    }

    private sealed record MetaError(int? Code)
    {
        public static readonly MetaError None = new((int?)null);

        public static MetaError From(byte[] body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Object)
                {
                    return None;
                }

                var code = error.TryGetProperty("code", out var codeElement)
                           && codeElement.TryGetInt32(out var parsedCode)
                    ? parsedCode
                    : (int?)null;

                return new MetaError(code);
            }
            catch (JsonException)
            {
                return None;
            }
        }
    }
}
