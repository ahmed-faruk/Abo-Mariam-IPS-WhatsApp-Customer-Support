using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Host;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Demo;

/// <summary>
/// The signed webhook capture and replay tool. A capture file is written once, and replaying its exact
/// bytes through the real host is deduplicated by the Inbox, so a duplicate delivery produces exactly one
/// reply.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WebhookCaptureTests(PostgresContainerFixture postgres)
{
    private const string Customer = "201000000001";
    private const string PhoneNumberId = "123456789";
    private const string AppSecret = "test-app-secret";
    private const string Greeting = "السلام عليكم";

    public static TheoryData<string[]> CaptureUsageErrors =>
    [
        ["capture"],
        ["capture", "replay"],
        ["capture", "create", "--from", Customer, "--text", Greeting],
        ["capture", "create", "--from", "12345", "--text", Greeting, "--out", "x.json"],
        ["capture", "create", "--from", Customer, "--text", " ", "--out", "x.json"],
        ["capture", "create", "--from", Customer, "--text", Greeting, "--out", "x.json", "--out", "y.json"],
        ["capture", "send"],
        ["capture", "send", "--file", "x.json", "--url", "ftp://127.0.0.1/hook"],
        ["capture", "send", "--file", "x.json", "--secret", "leak"],
    ];

    [Fact]
    public void W1_signature_is_the_lowercase_hex_hmac_sha256_of_the_exact_bytes()
    {
        // The published HMAC-SHA256 test vector, so the expected value does not come from the code under test.
        var body = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");

        Assert.Equal(
            "sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            WebhookCapture.Sign(body, "key"));
    }

    [Fact]
    public async Task W2_replaying_one_capture_twice_produces_exactly_one_reply()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        var nlu = new ScriptedAiNluClient().With(Greeting, NluAnalysisResult.Success(GreetingInterpretation()));
        var sender = new RecordingOutboundSender();

        await using var factory = new ApplicationHostFactory(
            connectionString,
            StubOllamaTagsHandler.WithNames("qwen3.5:2b-q4_K_M"),
            services =>
            {
                services.AddSingleton<IAiNluClient>(nlu);
                services.AddSingleton<IOutboundMessageSender>(sender);
            });
        using var client = factory.CreateClient();
        var url = new Uri(client.BaseAddress!, "/api/whatsapp/webhook");

        using var directory = new TemporaryDirectory();
        var file = directory.File("greeting.json");
        var providerMessageId = await WebhookCapture.CreateAsync(
            Customer, Greeting, PhoneNumberId, null, file, DateTimeOffset.UtcNow);

        var first = await WebhookCapture.SendAsync(client, url, file, AppSecret);
        var second = await WebhookCapture.SendAsync(client, url, file, AppSecret);

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.OK, second);
        Assert.Equal(
            $"1|{providerMessageId}",
            await database.ScalarAsync("SELECT count(*) || '|' || min(provider_message_id) FROM messaging.inbox_message"));

        await WaitUntilAsync(
            database,
            "SELECT ((SELECT count(*) FROM messaging.outbox_message WHERE delivery_status = 'Sent') = 1 "
            + "AND (SELECT count(*) FROM messaging.outbox_message WHERE delivery_status <> 'Sent') = 0 "
            + "AND (SELECT count(*) FROM messaging.inbox_message WHERE processing_status = 'Processed') = 1 "
            + "AND (SELECT count(*) FROM messaging.inbox_message WHERE processing_status <> 'Processed') = 0)::text");

        var reply = Assert.Single(sender.Sent);
        Assert.Equal(Customer, reply.CustomerExternalId);
        Assert.False(string.IsNullOrWhiteSpace(reply.Body));
        Assert.Equal([Greeting], nlu.Messages);
    }

    [Fact]
    public async Task W3_a_tampered_capture_is_rejected_with_403_and_never_queued()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);

        await using var factory = new ApplicationHostFactory(connectionString, StubOllamaTagsHandler.WithNames("qwen3.5:2b-q4_K_M"));
        using var client = factory.CreateClient();

        using var directory = new TemporaryDirectory();
        var file = directory.File("greeting.json");
        await WebhookCapture.CreateAsync(Customer, Greeting, PhoneNumberId, "wamid.DEMO.TAMPER", file, DateTimeOffset.UtcNow);

        var original = await File.ReadAllBytesAsync(file);
        var tampered = original.ToArray();
        var index = Array.IndexOf(tampered, (byte)'D');
        tampered[index] = (byte)'X';

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook");
        request.Content = new ByteArrayContent(tampered);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(WebhookCapture.SignatureHeader, WebhookCapture.Sign(original, AppSecret));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("0", await database.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
    }

    [Fact]
    public async Task W4_a_capture_file_is_the_meta_text_payload_written_once_without_a_bom()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("capture.json");
        var now = new DateTimeOffset(2026, 9, 25, 16, 30, 0, TimeSpan.Zero);

        var id = await WebhookCapture.CreateAsync(Customer, Greeting, PhoneNumberId, null, file, now);

        var bytes = await File.ReadAllBytesAsync(file);
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Matches(@"^wamid\.DEMO\.20260925163000\.[0-9a-f]{8}\z", id);

        using var document = JsonDocument.Parse(bytes);
        var value = document.RootElement.GetProperty("entry")[0].GetProperty("changes")[0].GetProperty("value");
        var message = value.GetProperty("messages")[0];

        Assert.Equal("whatsapp_business_account", document.RootElement.GetProperty("object").GetString());
        Assert.Equal(PhoneNumberId, value.GetProperty("metadata").GetProperty("phone_number_id").GetString());
        Assert.Equal(Customer, message.GetProperty("from").GetString());
        Assert.Equal(id, message.GetProperty("id").GetString());
        Assert.Equal(now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), message.GetProperty("timestamp").GetString());
        Assert.Equal("text", message.GetProperty("type").GetString());
        Assert.Equal(Greeting, message.GetProperty("text").GetProperty("body").GetString());

        await Assert.ThrowsAsync<IOException>(() =>
            WebhookCapture.CreateAsync(Customer, "other", PhoneNumberId, "wamid.other", file, now));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task W5_capture_commands_report_create_and_a_failed_send_through_exit_codes()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.File("cli.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:PhoneNumberId"] = PhoneNumberId,
                ["WhatsApp:AppSecret"] = AppSecret,
            })
            .Build();

        var create = await RunAsync(
            configuration,
            ["capture", "create", "--from", Customer, "--text", Greeting, "--out", file, "--id", "wamid.DEMO.CLI"]);
        var send = await RunAsync(
            configuration,
            ["capture", "send", "--file", file, "--url", "http://127.0.0.1:1/api/whatsapp/webhook"]);

        Assert.Equal(DemoCli.Success, create.ExitCode);
        Assert.StartsWith($"CAPTURE CREATED: file={file} provider_message_id=wamid.DEMO.CLI sha256=", create.Output, StringComparison.Ordinal);
        Assert.Equal(DemoCli.SendFailed, send.ExitCode);
        Assert.StartsWith("SEND FAILED: HttpRequestException:", send.Error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CaptureUsageErrors))]
    public async Task W6_invalid_capture_arguments_are_a_usage_error(string[] args)
    {
        var run = await RunAsync(new ConfigurationBuilder().AddInMemoryCollection([]).Build(), args);

        Assert.Equal(DemoCli.UsageError, run.ExitCode);
        Assert.StartsWith("USAGE ERROR:", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    private static NluInterpretation GreetingInterpretation() => new()
    {
        Intent = NluIntent.Greeting,
        RequiredPorts = [],
        Grades = [],
        BudgetType = NluBudgetType.None,
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IConfiguration configuration, string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await DemoCli.RunAsync(args, configuration, output, error);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task WaitUntilAsync(DatabaseCatalogReader database, string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (await database.ScalarAsync(condition) != "true")
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The condition did not hold within 30 seconds: {condition}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>A private directory for capture files, removed with everything in it.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("demoops-capture-").FullName;

        public string File(string name) => Path.Combine(path, name);

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
