using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

    /// <summary>The business number this host is configured to receive for.</summary>
    private const string PhoneNumberId = "123";

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
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004001","id":"wamid.http-1","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
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
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004002","id":"wamid.http-2","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
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
    public async Task A_signed_callback_for_another_phone_number_is_acknowledged_without_inbox_work()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550009999","phone_number_id":"456"},"messages":[{"from":"20100004008","id":"wamid.http-8","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        // The callback is authentic and well formed, but it was received by another subscribed business
        // number, so this deployment acknowledges it without retries and stores nothing.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task Multi_message_retry_deduplicates_the_already_committed_message_and_accepts_the_missing_one()
    {
        using var server = Server();
        const string retryBody = """
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004003","id":"wamid.http-3a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100004003","id":"wamid.http-3b","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;
        await InsertPrefixAsync(retryBody);
        using var request = SignedRequest(retryBody);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task The_webhook_cannot_answer_while_the_durable_acceptance_is_still_blocked()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004005","id":"wamid.http-5","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        await using var acceptance = await BlockEnvelopeInsertAsync();

        var responseTask = server.CreateClient().SendAsync(request);

        // The endpoint is proven to have reached the blocked envelope insert, not merely assumed to
        // have reached it after a delay: the insert advances a test-only sequence marker before it
        // blocks, and a sequence increment is visible to this connection while that insert's own
        // transaction is still uncommitted.
        await WaitUntilEnvelopeInsertIsReachedAsync(acceptance.ReachedBaseline, responseTask);

        // The acceptance is held inside its database transaction, so the endpoint cannot have
        // answered yet: the 200 follows the commit, it is not the handler returning.
        Assert.False(responseTask.IsCompleted);
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));

        await acceptance.ReleaseAsync();

        var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // An independent connection sees the committed envelope and its Inbox row.
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-5'"));
    }

    [Fact]
    public async Task A_real_persistence_failure_cannot_produce_http_200()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004006","id":"wamid.http-6","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;

        await catalog.ExecuteAsync(
            """
            CREATE FUNCTION messaging.test_reject_every_inbox_row() RETURNS trigger AS $function$
            BEGIN
                RAISE EXCEPTION 'test fault: inbox insert rejected';
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER test_reject_every_inbox_row BEFORE INSERT ON messaging.inbox_message
            FOR EACH ROW EXECUTE FUNCTION messaging.test_reject_every_inbox_row();
            """);

        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // The rejected acceptance rolled back whole: neither the Inbox row nor its envelope survives.
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task A_committed_prefix_is_deduplicated_when_the_retry_accepts_the_message_that_failed()
    {
        using var server = Server();
        const string body = """
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001234","phone_number_id":"123"},"messages":[{"from":"20100004007","id":"wamid.http-7a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100004007","id":"wamid.http-7b","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;

        // Only the second message is rejected by the database, so the first genuinely commits inside
        // the first request while the second genuinely rolls back.
        await catalog.ExecuteAsync(
            """
            CREATE FUNCTION messaging.test_reject_second_message() RETURNS trigger AS $function$
            BEGIN
                IF NEW.provider_message_id = 'wamid.http-7b' THEN
                    RAISE EXCEPTION 'test fault: second message rejected';
                END IF;

                RETURN NEW;
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER test_reject_second_message BEFORE INSERT ON messaging.inbox_message
            FOR EACH ROW EXECUTE FUNCTION messaging.test_reject_second_message();
            """);

        using var first = SignedRequest(body);
        var firstResponse = await server.CreateClient().SendAsync(first);

        Assert.Equal(HttpStatusCode.InternalServerError, firstResponse.StatusCode);
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-7a'"));
        Assert.Equal("0", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-7b'"));

        await catalog.ExecuteAsync("DROP TRIGGER test_reject_second_message ON messaging.inbox_message");

        using var retry = SignedRequest(body);
        var retryResponse = await server.CreateClient().SendAsync(retry);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-7a'"));
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.http-7b'"));
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    /// <summary>
    /// Holds the real durable acceptance of this database inside its transaction: the envelope insert
    /// first advances a test-only sequence marker and then blocks on an advisory lock this test keeps
    /// held, so the test can observe that the request reached the insert while it is still blocked.
    /// </summary>
    private async Task<BlockedAcceptance> BlockEnvelopeInsertAsync()
    {
        await catalog.ExecuteAsync(
            """
            CREATE SEQUENCE messaging.test_envelope_insert_reached;
            SELECT nextval('messaging.test_envelope_insert_reached');

            CREATE FUNCTION messaging.test_block_envelope_insert() RETURNS trigger AS $function$
            BEGIN
                PERFORM nextval('messaging.test_envelope_insert_reached');
                PERFORM pg_advisory_xact_lock(918273645);

                RETURN NEW;
            END;
            $function$ LANGUAGE plpgsql;

            CREATE TRIGGER test_block_envelope_insert BEFORE INSERT ON messaging.webhook_envelope
            FOR EACH ROW EXECUTE FUNCTION messaging.test_block_envelope_insert();
            """);

        var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(918273645)", connection);

        await command.ExecuteNonQueryAsync();

        var reachedBaseline = long.Parse(
            await catalog.ScalarAsync("SELECT last_value FROM messaging.test_envelope_insert_reached"),
            CultureInfo.InvariantCulture);

        return new BlockedAcceptance(connection, reachedBaseline);
    }

    /// <summary>
    /// Waits for the durable acceptance to report that it reached the blocked insert, using the
    /// database as the synchronization point instead of a fixed delay. The timeout is only a guard so
    /// a request that never reaches persistence fails the test instead of hanging it.
    /// </summary>
    private async Task WaitUntilEnvelopeInsertIsReachedAsync(long baseline, Task<HttpResponseMessage> responseTask)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (true)
        {
            var reached = long.Parse(
                await catalog.ScalarAsync("SELECT last_value FROM messaging.test_envelope_insert_reached"),
                CultureInfo.InvariantCulture);

            if (reached > baseline)
            {
                return;
            }

            Assert.False(
                responseTask.IsCompleted,
                "The webhook answered before it reached the blocked durable acceptance.");

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"The webhook never reached the blocked durable acceptance; the marker stayed at {baseline}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }

    /// <summary>
    /// The advisory lock this test holds to keep one envelope insert inside its transaction, released
    /// explicitly so the request can finish, and released again on dispose so a failed assertion
    /// cannot leak the lock.
    /// </summary>
    private sealed class BlockedAcceptance(NpgsqlConnection connection, long reachedBaseline) : IAsyncDisposable
    {
        private bool released;

        public long ReachedBaseline { get; } = reachedBaseline;

        public async Task ReleaseAsync()
        {
            if (released)
            {
                return;
            }

            released = true;

            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(918273645)", connection);

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            await connection.DisposeAsync();
        }
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
                        options.PhoneNumberId = PhoneNumberId;
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
