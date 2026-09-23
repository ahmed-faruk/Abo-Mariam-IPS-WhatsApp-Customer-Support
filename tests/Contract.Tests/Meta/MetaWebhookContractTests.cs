using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;

namespace WhatsAppMonitorAssistant.Contract.Tests.Meta;

#pragma warning disable ASPDEPR004, ASPDEPR008

public sealed class MetaWebhookContractTests
{
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "verify-token";

    [Fact]
    public async Task Verification_returns_the_challenge_when_the_token_matches()
    {
        using var server = Server();
        var response = await server.CreateClient().GetAsync(
            $"{WhatsAppWebhookEndpoints.Path}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Verification_fails_when_the_token_does_not_match()
    {
        using var server = Server();
        var response = await server.CreateClient().GetAsync(
            $"{WhatsAppWebhookEndpoints.Path}?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_signature_is_rejected_before_persistence()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        using var request = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhookEndpoints.Path)
        {
            Content = new StringContent(TextPayload("wamid.missing-sig", "20100000991", "hello"), Encoding.UTF8, "application/json"),
        };

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_before_persistence()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = TextPayload("wamid.1", "20100000001", "hello");
        using var request = SignedRequest(body, "wrong-secret");

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Altering_the_raw_payload_after_signature_rejects_the_request()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var signedBody = TextPayload("wamid.altered", "20100000992", "hello");
        var alteredBody = TextPayload("wamid.altered", "20100000992", "hello ");
        using var request = SignedRequest(signedBody);
        request.Content = new StringContent(alteredBody, Encoding.UTF8, "application/json");

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Valid_text_message_is_durably_accepted()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = TextPayload("wamid.2", "20100000002", "hello");
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(queue.Envelopes);
        Assert.Equal(body, envelope.RawBody);
        Assert.Equal("wamid.2", envelope.ProviderMessageId);
        Assert.Equal("20100000002", envelope.CustomerExternalId);
        Assert.Equal("text", envelope.MessageType);
        Assert.Equal("hello", envelope.Body);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, envelope.ProviderTimestamp);
    }

    [Fact]
    public async Task Unsupported_customer_media_is_queued_without_a_fabricated_body()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = MessagePayload("""{"from":"20100000003","id":"wamid.3","timestamp":"1700000000","type":"image","image":{"id":"media"}}""");
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(queue.Envelopes);
        Assert.Equal("image", envelope.MessageType);
        Assert.Null(envelope.Body);
    }

    [Fact]
    public async Task Status_only_notifications_acknowledge_without_inbox_work()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        const string body = """
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"wamid.sent","status":"sent"}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Messages_and_statuses_can_coexist()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = """
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"sent","status":"sent"}],"messages":[{"from":"20100000004","id":"wamid.4","timestamp":"1700000000","type":"text","text":{"body":"hello"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(queue.Envelopes);
    }

    [Fact]
    public async Task Multiple_customer_messages_are_all_accepted_with_the_same_raw_body()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100000005","id":"wamid.5a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100000005","id":"wamid.5b","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["wamid.5a", "wamid.5b"], queue.Envelopes.Select(envelope => envelope.ProviderMessageId));
        Assert.All(queue.Envelopes, envelope => Assert.Equal(body, envelope.RawBody));
    }

    [Fact]
    public async Task Malformed_relevant_message_payload_is_rejected_before_any_persistence()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100000006","id":"wamid.6a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100000006","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"entry":"not-an-array"}""")]
    [InlineData("""{"entry":[[]]}""")]
    [InlineData("""{"entry":[{"changes":{}}]}""")]
    [InlineData("""{"entry":[{"changes":[{"value":[]}]}]}""")]
    [InlineData("""{"entry":[{"changes":[{"value":{"messages":{}}}]}]}""")]
    [InlineData("""{"entry":[{"changes":[{"value":{"messages":[[]]}}]}]}""")]
    public async Task Structurally_invalid_payloads_are_rejected_with_400(string body)
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Theory]
    [InlineData("\"a string document\"")]
    [InlineData("17")]
    public async Task A_non_object_document_is_rejected_with_400(string body)
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Theory]
    [InlineData("not-a-timestamp")]
    [InlineData("253402300800")]
    [InlineData("-1")]
    public async Task An_unusable_provider_timestamp_is_rejected_with_400(string timestamp)
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = MessagePayload(
            "{\"from\":\"20100000008\",\"id\":\"wamid.8\",\"timestamp\":\"" + timestamp
            + "\",\"type\":\"text\",\"text\":{\"body\":\"hello\"}}");
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Persistence_failure_prevents_http_200_after_the_successful_prefix()
    {
        var queue = new CapturingInboundQueue { FailOnAttempt = 2 };
        using var server = Server(queue);
        var body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"20100000007","id":"wamid.7a","timestamp":"1700000000","type":"text","text":{"body":"one"}},{"from":"20100000007","id":"wamid.7b","timestamp":"1700000001","type":"text","text":{"body":"two"}}]}}]}]}
            """;
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(["wamid.7a"], queue.Envelopes.Select(envelope => envelope.ProviderMessageId));
    }

    [Fact]
    public async Task Oversized_payload_is_rejected_before_persistence()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue, maxWebhookBodyBytes: WhatsAppOptions.DefaultMaxWebhookBodyBytes);
        var body = new string('x', WhatsAppOptions.DefaultMaxWebhookBodyBytes + 1);
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Webhook_rate_limit_rejects_excess_requests()
    {
        using var server = Server(permitLimit: 1);
        var body = TextPayload("wamid.rate-1", "20100000993", "hello");

        using var first = SignedRequest(body);
        using var second = SignedRequest(body);

        Assert.Equal(HttpStatusCode.OK, (await server.CreateClient().SendAsync(first)).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await server.CreateClient().SendAsync(second)).StatusCode);
    }

    private static TestServer Server(
        CapturingInboundQueue? queue = null,
        int maxWebhookBodyBytes = WhatsAppOptions.DefaultMaxWebhookBodyBytes,
        int permitLimit = 1000)
    {
        queue ??= new CapturingInboundQueue();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.AddFixedWindowLimiter(WhatsAppRateLimit.PolicyName, limiter =>
                    {
                        limiter.PermitLimit = permitLimit;
                        limiter.Window = TimeSpan.FromMinutes(1);
                    });
                });
                services.AddSingleton(new WhatsAppOptions
                {
                    ApiVersion = "v23.0",
                    PhoneNumberId = "123",
                    VerifyToken = VerifyToken,
                    AppSecret = AppSecret,
                    AccessToken = "access-token",
                    MaxWebhookBodyBytes = maxWebhookBodyBytes,
                    WebhookPermitLimit = permitLimit,
                });
                services.AddSingleton<IInboundMessageQueue>(queue);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapWhatsAppWebhookEndpoints());
            });

        return new TestServer(builder);
    }

    private static HttpRequestMessage SignedRequest(string body, string secret = AppSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhookEndpoints.Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", Signature(body, secret));

        return request;
    }

    private static string Signature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))}";
    }

    private static string TextPayload(string id, string from, string text) =>
        MessagePayload(
            "{\"from\":\"" + from + "\",\"id\":\"" + id
            + "\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\""
            + text + "\"}}");

    private static string MessagePayload(string messageJson) =>
        "{\"entry\":[{\"changes\":[{\"value\":{\"messages\":[" + messageJson + "]}}]}]}";

    private sealed class CapturingInboundQueue : IInboundMessageQueue
    {
        private int attempts;

        public List<InboundMessageEnvelope> Envelopes { get; } = [];

        public int? FailOnAttempt { get; set; }

        public Task<InboundEnqueueResult> EnqueueAsync(
            InboundMessageEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            attempts++;

            if (FailOnAttempt == attempts)
            {
                throw new InvalidOperationException("database commit failed");
            }

            Envelopes.Add(envelope);

            return Task.FromResult(new InboundEnqueueResult(Envelopes.Count, IsDuplicate: false));
        }
    }
}

#pragma warning restore ASPDEPR004, ASPDEPR008
