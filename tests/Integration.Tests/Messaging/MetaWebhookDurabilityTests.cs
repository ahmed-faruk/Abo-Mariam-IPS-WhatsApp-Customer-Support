using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

#pragma warning disable ASPDEPR004, ASPDEPR008

[Collection(PostgresCollection.Name)]
public sealed class MetaWebhookDurabilityTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string AppSecret = "test-app-secret";
    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Webhook_returns_200_only_after_the_inbox_row_is_committed()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100004001","id":"wamid.http-1","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-1'"));
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task Duplicate_webhook_delivery_returns_200_without_a_second_inbox_row()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100004002","id":"wamid.http-2","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;

        using var first = SignedRequest(body);
        using var second = SignedRequest(body);

        Assert.Equal(HttpStatusCode.OK, (await server.CreateClient().SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.CreateClient().SendAsync(second)).StatusCode);
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-2'"));
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task Multi_message_retry_deduplicates_the_already_committed_message_and_accepts_the_missing_one()
    {
        using var server = Server();
        const string retryBody = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100004003","id":"wamid.http-3a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100004003","id":"wamid.http-3b","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;
        await InsertPrefixAsync(retryBody);
        using var request = SignedRequest(retryBody);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    private TestServer Server()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddMessagingModule(
                    connectionString,
                    configure: null,
                    configureWhatsApp: options =>
                    {
                        options.ApiVersion = "v23.0";
                        options.PhoneNumberId = "123";
                        options.WabaId = "456";
                        options.VerifyToken = "verify";
                        options.AppSecret = AppSecret;
                        options.AccessToken = "access";
                        options.WebhookPermitLimit = 1000;
                    });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapWhatsAppWebhookEndpoints());
            });

        return new TestServer(builder);
    }

    private async Task InsertPrefixAsync(string rawBody)
    {
        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.http-3a",
            "20100004003",
            "text",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime,
            "one"));
    }

    private static HttpRequestMessage SignedRequest(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhookEndpoints.Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", Signature(body));

        return request;
    }

    private static string Signature(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));

        return $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))}";
    }
}

#pragma warning restore ASPDEPR004, ASPDEPR008
