using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Meta;

internal sealed class MetaOutboundMessageSender(HttpClient httpClient, WhatsAppOptions options)
    : IOutboundMessageSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<int> RetryableMetaCodes = [2, 4, 80007, 130429, 131016, 131056];
    private static readonly HashSet<int> PermanentMetaCodes = [100, 190, 130403, 131008, 131009, 131047];

    public async Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body.Length > WhatsAppOptions.MaxTextBodyLength)
        {
            return OutboundSendResult.PermanentFailure("Meta text body exceeds 4096 characters.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.facebook.com/{options.ApiVersion}/{options.PhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreateTextPayload(message), JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return OutboundSendResult.Unknown($"Unknown provider outcome: {Sanitize(exception.Message)}");
        }

        using (response)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return TryReadProviderMessageId(body, out var providerMessageId)
                    ? OutboundSendResult.Sent(providerMessageId)
                    : OutboundSendResult.Unknown("Unknown provider outcome: Meta success did not include a message id.");
            }

            var retryAfter = RetryAfter(response);
            var error = MetaError.From(body);
            var diagnostic = BoundedDiagnostic(response.StatusCode, error);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return OutboundSendResult.RetryableFailure(diagnostic, retryAfter);
            }

            if (error.Code is int code)
            {
                if (RetryableMetaCodes.Contains(code))
                {
                    return OutboundSendResult.RetryableFailure(diagnostic, retryAfter);
                }

                if (PermanentMetaCodes.Contains(code))
                {
                    return OutboundSendResult.PermanentFailure(diagnostic);
                }
            }

            return (int)response.StatusCode >= 500
                ? OutboundSendResult.Unknown($"Unknown provider outcome: {diagnostic}")
                : OutboundSendResult.PermanentFailure(diagnostic);
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

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return body.Length <= 1000 ? body : body[..1000];
    }

    private static bool TryReadProviderMessageId(string body, out string providerMessageId)
    {
        providerMessageId = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array
                || messages.GetArrayLength() == 0)
            {
                return false;
            }

            var first = messages[0];

            if (!first.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
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

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;

            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    private static string BoundedDiagnostic(HttpStatusCode statusCode, MetaError error)
    {
        var status = ((int)statusCode).ToString(CultureInfo.InvariantCulture);
        var code = error.Code?.ToString(CultureInfo.InvariantCulture) ?? "none";
        var message = string.IsNullOrWhiteSpace(error.Message) ? "Meta returned an error." : error.Message;

        return Sanitize($"Meta HTTP {status}; code {code}; {message}");
    }

    private static string Sanitize(string value)
    {
        var sanitized = value.ReplaceLineEndings(" ").Trim();

        return sanitized.Length <= 300 ? sanitized : sanitized[..300];
    }

    private sealed record MetaError(int? Code, string? Message)
    {
        public static MetaError From(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Object)
                {
                    return new MetaError(null, null);
                }

                var code = error.TryGetProperty("code", out var codeElement)
                           && codeElement.TryGetInt32(out var parsedCode)
                    ? parsedCode
                    : (int?)null;
                var message = error.TryGetProperty("message", out var messageElement)
                              && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : null;

                return new MetaError(code, message);
            }
            catch (JsonException)
            {
                return new MetaError(null, null);
            }
        }
    }
}
