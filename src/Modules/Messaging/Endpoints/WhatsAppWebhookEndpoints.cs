using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;

public static class WhatsAppWebhookEndpoints
{
    public const string Path = "/api/whatsapp/webhook";
    private const string SignatureHeader = "X-Hub-Signature-256";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> IgnoredMessageTypes = ["system"];

    /// <summary>
    /// The largest Unix second a <see cref="DateTimeOffset"/> can represent, so no provider timestamp
    /// in a payload can overflow the conversion that turns it into a stored instant.
    /// </summary>
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static IEndpointRouteBuilder MapWhatsAppWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, VerifyAsync);
        endpoints.MapPost(Path, AcceptAsync);

        return endpoints;
    }

    private static IResult VerifyAsync(HttpRequest request, WhatsAppOptions options)
    {
        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();

        if (string.Equals(mode, "subscribe", StringComparison.Ordinal)
            && string.Equals(token, options.VerifyToken, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(challenge))
        {
            return Results.Text(challenge, "text/plain", Encoding.UTF8);
        }

        return Results.BadRequest();
    }

    private static async Task<IResult> AcceptAsync(
        HttpRequest request,
        WhatsAppOptions options,
        IInboundMessageQueue queue,
        WhatsAppWebhookDeliveryLimiter deliveryLimiter,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > options.MaxWebhookBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var bodyBytes = await ReadBoundedBodyAsync(request, options.MaxWebhookBodyBytes, cancellationToken);

        if (bodyBytes is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!SignatureIsValid(request, options, bodyBytes))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // The delivery budget belongs to the authenticated Meta traffic only, so it is taken after the
        // signature proves this request really came from Meta: public traffic that fails the check above
        // can never exhaust the permits a genuine callback needs.
        if (!deliveryLimiter.TryAcquire())
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        string rawBody;

        try
        {
            rawBody = StrictUtf8.GetString(bodyBytes);
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest();
        }

        IReadOnlyList<ParsedInboundMessage> messages;

        try
        {
            messages = ParseCustomerMessages(rawBody);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (FormatException)
        {
            return Results.BadRequest();
        }

        try
        {
            foreach (var message in messages)
            {
                await queue.EnqueueAsync(
                    new InboundMessageEnvelope(
                        RawBody: rawBody,
                        ProviderMessageId: message.ProviderMessageId,
                        CustomerExternalId: message.CustomerExternalId,
                        MessageType: message.MessageType,
                        ProviderTimestamp: message.ProviderTimestampUtc,
                        Body: message.Body),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream(capacity: Math.Min(maxBytes, 81920));
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return output.ToArray();
            }

            total += read;

            if (total > maxBytes)
            {
                return null;
            }

            output.Write(buffer.AsSpan(0, read));
        }
    }

    private static bool SignatureIsValid(HttpRequest request, WhatsAppOptions options, byte[] bodyBytes)
    {
        var header = request.Headers[SignatureHeader].ToString();

        if (!header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hex = header["sha256=".Length..];

        if (hex.Length != 64)
        {
            return false;
        }

        byte[] expected;

        try
        {
            expected = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.AppSecret));
        var actual = hmac.ComputeHash(bodyBytes);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static IReadOnlyList<ParsedInboundMessage> ParseCustomerMessages(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Meta payload must be a JSON object.");
        }

        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The Meta payload must contain entry[].");
        }

        var messages = new List<ParsedInboundMessage>();

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The Meta entry item must be an object.");
            }

            if (!entry.TryGetProperty("changes", out var changes))
            {
                continue;
            }

            if (changes.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The Meta changes property must be an array.");
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("The Meta change item must be an object.");
                }

                if (!change.TryGetProperty("value", out var value))
                {
                    continue;
                }

                if (value.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("The Meta change value must be an object.");
                }

                if (!value.TryGetProperty("messages", out var messageElements))
                {
                    continue;
                }

                if (messageElements.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("The Meta messages property must be an array.");
                }

                foreach (var message in messageElements.EnumerateArray())
                {
                    if (TryParseCustomerMessage(message, out var parsed))
                    {
                        messages.Add(parsed);
                    }
                }
            }
        }

        return messages;
    }

    private static bool TryParseCustomerMessage(JsonElement message, out ParsedInboundMessage parsed)
    {
        parsed = default!;

        if (message.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Meta message item must be an object.");
        }

        var type = RequiredString(message, "type");

        if (IgnoredMessageTypes.Contains(type))
        {
            return false;
        }

        var providerMessageId = RequiredString(message, "id");
        var customerExternalId = RequiredString(message, "from");
        var timestamp = RequiredUnixTimestamp(message);
        var body = type == "text" ? RequiredTextBody(message) : null;

        parsed = new ParsedInboundMessage(providerMessageId, customerExternalId, type, timestamp, body);

        return true;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"The Meta message property '{propertyName}' is required.");
        }

        return value.GetString()!;
    }

    private static DateTime RequiredUnixTimestamp(JsonElement message)
    {
        var timestamp = RequiredString(message, "timestamp");

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)
            || unixSeconds < 0
            || unixSeconds > MaxUnixSeconds)
        {
            throw new FormatException(
                "The Meta message timestamp must be a Unix timestamp inside the supported range.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
    }

    private static string RequiredTextBody(JsonElement message)
    {
        if (!message.TryGetProperty("text", out var text)
            || text.ValueKind != JsonValueKind.Object
            || !text.TryGetProperty("body", out var body)
            || body.ValueKind != JsonValueKind.String
            || body.GetString() is null)
        {
            throw new FormatException("A Meta text message must contain text.body.");
        }

        return body.GetString()!;
    }

    private sealed record ParsedInboundMessage(
        string ProviderMessageId,
        string CustomerExternalId,
        string MessageType,
        DateTime ProviderTimestampUtc,
        string? Body);
}
