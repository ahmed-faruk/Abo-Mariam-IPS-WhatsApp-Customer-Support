using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace WhatsAppMonitorAssistant.Tools.DemoOps;

/// <summary>
/// Signed webhook capture and replay of docs/TECHNICAL.md section 36.5. A capture file holds the exact
/// payload bytes once; every send posts those same bytes with a fresh HMAC signature, so a replay
/// carries the identical payload and provider message id that the Inbox deduplicates. The stored
/// <c>jsonb</c> envelope is never used, because it cannot reproduce the original byte stream.
/// </summary>
public static partial class WebhookCapture
{
    public const string SignatureHeader = "X-Hub-Signature-256";

    public const string DefaultUrl = "http://127.0.0.1:5000/api/whatsapp/webhook";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a new capture file holding one Meta text-message webhook and returns its provider message
    /// id. The file must not exist yet, so a capture is never silently replaced.
    /// </summary>
    public static async Task<string> CreateAsync(
        string from,
        string text,
        string phoneNumberId,
        string? providerMessageId,
        string outPath,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumberId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var id = providerMessageId
            ?? string.Create(
                CultureInfo.InvariantCulture,
                $"wamid.DEMO.{now.UtcDateTime:yyyyMMddHHmmss}.{RandomNumberGenerator.GetHexString(8, lowercase: true)}");

        var payload = new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = "DEMO",
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["value"] = new JsonObject
                    {
                        ["messaging_product"] = "whatsapp",
                        ["metadata"] = new JsonObject
                        {
                            ["display_phone_number"] = "DEMO",
                            ["phone_number_id"] = phoneNumberId,
                        },
                        ["contacts"] = new JsonArray(new JsonObject
                        {
                            ["profile"] = new JsonObject { ["name"] = "Demo" },
                            ["wa_id"] = from,
                        }),
                        ["messages"] = new JsonArray(new JsonObject
                        {
                            ["from"] = from,
                            ["id"] = id,
                            ["timestamp"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                            ["type"] = "text",
                            ["text"] = new JsonObject { ["body"] = text },
                        }),
                    },
                    ["field"] = "messages",
                }),
            }),
        };

        var bytes = Utf8WithoutBom.GetBytes(payload.ToJsonString());

        await using var file = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(bytes, cancellationToken);

        return id;
    }

    /// <summary>The Meta signature of a body: <c>sha256=</c> and the lowercase hex HMAC-SHA256.</summary>
    public static string Sign(ReadOnlySpan<byte> body, string appSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSecret);

        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));
    }

    /// <summary>Posts the capture file's bytes verbatim with their signature and returns the HTTP status.</summary>
    public static async Task<HttpStatusCode> SendAsync(
        HttpClient client,
        Uri url,
        string filePath,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(SignatureHeader, Sign(bytes, appSecret));

        using var response = await client.SendAsync(request, cancellationToken);

        return response.StatusCode;
    }

    /// <summary>
    /// The <c>capture create</c> and <c>capture send</c> commands. Arguments are validated before any
    /// file or network access, and a send succeeds only on HTTP 200.
    /// </summary>
    internal static async Task<int> RunCommandAsync(
        IReadOnlyList<string> args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParse(args, out var options, out var problem))
        {
            error.WriteLine($"USAGE ERROR: {problem}");
            error.WriteLine(DemoCli.UsageText);

            return DemoCli.UsageError;
        }

        return args[1] == "create"
            ? await CreateCommandAsync(options, configuration, output, cancellationToken)
            : await SendCommandAsync(options, configuration, output, error, cancellationToken);
    }

    private static async Task<int> CreateCommandAsync(
        Dictionary<string, string> options,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var phoneNumberId = RequiredSetting(configuration, "WhatsApp:PhoneNumberId");
        var id = await CreateAsync(
            options["--from"],
            options["--text"],
            phoneNumberId,
            options.GetValueOrDefault("--id"),
            options["--out"],
            DateTimeOffset.UtcNow,
            cancellationToken);

        var bytes = await File.ReadAllBytesAsync(options["--out"], cancellationToken);

        output.WriteLine($"CAPTURE CREATED: file={options["--out"]} provider_message_id={id} "
            + $"sha256={Convert.ToHexStringLower(SHA256.HashData(bytes))}");

        return DemoCli.Success;
    }

    private static async Task<int> SendCommandAsync(
        Dictionary<string, string> options,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var appSecret = RequiredSetting(configuration, "WhatsApp:AppSecret");
        var url = new Uri(options.GetValueOrDefault("--url") ?? DefaultUrl, UriKind.Absolute);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        try
        {
            var status = await SendAsync(client, url, options["--file"], appSecret, cancellationToken);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SEND HTTP {(int)status}"));

            return status == HttpStatusCode.OK ? DemoCli.Success : DemoCli.SendFailed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            error.WriteLine($"SEND FAILED: {exception.GetType().Name}: {exception.Message}");

            return DemoCli.SendFailed;
        }
    }

    private static string RequiredSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The setting '{key}' is not configured.")
            : value;
    }

    private static bool TryParse(IReadOnlyList<string> args, out Dictionary<string, string> options, out string problem)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        options = values;
        problem = string.Empty;

        var action = args.Count > 1 ? args[1] : string.Empty;
        string[] required;
        string[] allowed;

        switch (action)
        {
            case "create":
                required = ["--from", "--text", "--out"];
                allowed = ["--from", "--text", "--out", "--id"];
                break;
            case "send":
                required = ["--file"];
                allowed = ["--file", "--url"];
                break;
            default:
                problem = "capture needs 'create' or 'send'.";

                return false;
        }

        for (var index = 2; index < args.Count; index += 2)
        {
            var name = args[index];

            if (!allowed.Contains(name, StringComparer.Ordinal) || index + 1 >= args.Count)
            {
                problem = $"unexpected or incomplete argument '{name}' for capture {action}.";

                return false;
            }

            if (!options.TryAdd(name, args[index + 1]))
            {
                problem = $"the argument '{name}' is given twice.";

                return false;
            }
        }

        var missing = required.FirstOrDefault(name => !values.ContainsKey(name));

        if (missing is not null)
        {
            problem = $"capture {action} needs '{missing}'.";

            return false;
        }

        if (options.TryGetValue("--from", out var from) && !CustomerIdPattern().IsMatch(from))
        {
            problem = $"'{from}' is not a WhatsApp number of 8 to 15 digits.";

            return false;
        }

        var blank = values.Keys.FirstOrDefault(name => string.IsNullOrWhiteSpace(values[name]));

        if (blank is not null)
        {
            problem = $"the argument '{blank}' must not be blank.";

            return false;
        }

        if (options.TryGetValue("--url", out var url)
            && (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")))
        {
            problem = $"'{url}' is not an absolute http or https URL.";

            return false;
        }

        return true;
    }

    [GeneratedRegex(@"^[0-9]{8,15}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CustomerIdPattern();
}
