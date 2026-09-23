using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

    /// <summary>The business number this test host is configured to receive for.</summary>
    private const string PhoneNumberId = "123";

    /// <summary>A second subscribed business number this deployment must never answer for.</summary>
    private const string OtherPhoneNumberId = "456";

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
        var body = EntryWithValue(ValueWithMetadata(
            PhoneNumberId,
            """{"from":"20100000003","id":"wamid.3","timestamp":"1700000000","type":"image","image":{"id":"media"}}"""));
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
        var body = EntryWithValue(
            "{\"statuses\":[{\"id\":\"sent\",\"status\":\"sent\"}],\"metadata\":{\"display_phone_number\":\"15550001111\",\"phone_number_id\":\""
            + PhoneNumberId
            + "\"},\"messages\":["
            + TextMessage("wamid.4", "20100000004", "hello")
            + "]}");
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
        var body = EntryWithValue(ValueWithMetadata(
            PhoneNumberId,
            "{\"from\":\"20100000005\",\"id\":\"wamid.5a\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\"one\"}},"
            + "{\"from\":\"20100000005\",\"id\":\"wamid.5b\",\"timestamp\":\"1700000001\",\"type\":\"text\",\"text\":{\"body\":\"two\"}}"));
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["wamid.5a", "wamid.5b"], queue.Envelopes.Select(envelope => envelope.ProviderMessageId));
        Assert.All(queue.Envelopes, envelope => Assert.Equal(body, envelope.RawBody));
    }

    [Fact]
    public async Task A_message_addressed_to_the_configured_phone_number_is_enqueued_once()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = TextPayload("wamid.phone-match", "20100000020", "hello");
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = Assert.Single(queue.Envelopes);
        Assert.Equal("wamid.phone-match", accepted.ProviderMessageId);
    }

    [Fact]
    public async Task A_well_formed_callback_for_another_phone_number_is_acknowledged_without_inbox_work()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = EntryWithValue(
            ValueWithMetadata(OtherPhoneNumberId, TextMessage("wamid.phone-other", "20100000021", "hello")));
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        // The callback is authentic and well formed, it simply belongs to another business number, so
        // it is acknowledged without retries and without becoming this deployment's customer work.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Theory]
    [InlineData("{\"messages\":[%MESSAGE%]}")]
    [InlineData("{\"metadata\":\"not-an-object\",\"messages\":[%MESSAGE%]}")]
    [InlineData("{\"metadata\":{\"display_phone_number\":\"15550001111\"},\"messages\":[%MESSAGE%]}")]
    [InlineData("{\"metadata\":{\"display_phone_number\":\"15550001111\",\"phone_number_id\":123},\"messages\":[%MESSAGE%]}")]
    [InlineData("{\"metadata\":{\"display_phone_number\":\"15550001111\",\"phone_number_id\":\"\"},\"messages\":[%MESSAGE%]}")]
    [InlineData("{\"metadata\":{\"display_phone_number\":\"15550001111\",\"phone_number_id\":\"  \"},\"messages\":[%MESSAGE%]}")]
    public async Task A_message_collection_that_cannot_name_its_receiving_number_is_rejected_with_400(
        string valueTemplate)
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = EntryWithValue(
            valueTemplate.Replace("%MESSAGE%", TextMessage("wamid.phone-malformed", "20100000022", "hello"), StringComparison.Ordinal));
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        // The deployment cannot prove the messages are addressed to it, so the whole request is
        // malformed and nothing of it is persisted.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task Only_the_changes_addressed_to_the_configured_phone_number_are_enqueued()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = EntryWithChanges(
            ValueWithMetadata(OtherPhoneNumberId, TextMessage("wamid.phone-other", "20100000023", "hello")),
            ValueWithMetadata(PhoneNumberId, TextMessage("wamid.phone-mine", "20100000024", "hello")));
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = Assert.Single(queue.Envelopes);
        Assert.Equal("wamid.phone-mine", accepted.ProviderMessageId);
    }

    [Fact]
    public async Task A_later_malformed_change_prevents_persistence_of_an_earlier_valid_one()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = EntryWithChanges(
            ValueWithMetadata(PhoneNumberId, TextMessage("wamid.phone-first", "20100000025", "hello")),
            "{\"messages\":[" + TextMessage("wamid.phone-second", "20100000026", "hello") + "]}");
        using var request = SignedRequest(body);

        var response = await server.CreateClient().SendAsync(request);

        // The complete relevant structure is validated before anything is persisted, so a later
        // malformed collection cannot leave the earlier valid one durably accepted.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Envelopes);
    }

    [Fact]
    public async Task A_duplicate_delivery_is_acknowledged_without_a_second_work_item()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = TextPayload("wamid.duplicate", "20100000010", "hello");

        using var first = SignedRequest(body);
        var firstResponse = await server.CreateClient().SendAsync(first);
        using var duplicate = SignedRequest(body);
        var duplicateResponse = await server.CreateClient().SendAsync(duplicate);

        // Meta retries a delivery it did not see acknowledged: the retry is accepted, and the durable
        // provider-message deduplication of the queue leaves exactly one logical work item behind.
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var accepted = Assert.Single(queue.Envelopes);
        Assert.Equal("wamid.duplicate", accepted.ProviderMessageId);
    }

    [Fact]
    public async Task Malformed_relevant_message_payload_is_rejected_before_any_persistence()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue);
        var body = EntryWithValue(ValueWithMetadata(
            PhoneNumberId,
            "{\"from\":\"20100000006\",\"id\":\"wamid.6a\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\"one\"}},"
            + "{\"from\":\"20100000006\",\"timestamp\":\"1700000001\",\"type\":\"text\",\"text\":{\"body\":\"two\"}}"));
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
    // The message-item case carries the configured routing number, so it passes the phone-number guard
    // and reaches the guard that rejects a non-object item inside messages[] itself.
    [InlineData("""{"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"15550001111","phone_number_id":"123"},"messages":[[]]}}]}]}""")]
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
        var body = EntryWithValue(ValueWithMetadata(
            PhoneNumberId,
            "{\"from\":\"20100000008\",\"id\":\"wamid.8\",\"timestamp\":\"" + timestamp
            + "\",\"type\":\"text\",\"text\":{\"body\":\"hello\"}}"));
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
        var body = EntryWithValue(ValueWithMetadata(
            PhoneNumberId,
            "{\"from\":\"20100000007\",\"id\":\"wamid.7a\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\"one\"}},"
            + "{\"from\":\"20100000007\",\"id\":\"wamid.7b\",\"timestamp\":\"1700000001\",\"type\":\"text\",\"text\":{\"body\":\"two\"}}"));
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

    [Fact]
    public async Task Unauthenticated_traffic_cannot_consume_the_authenticated_delivery_budget()
    {
        var queue = new CapturingInboundQueue();
        using var server = Server(queue, permitLimit: 1);
        var body = TextPayload("wamid.budget", "20100000011", "hello");

        // Public traffic that fails the signature check is rejected without touching the delivery
        // budget, so it can never starve a genuine Meta callback of its permit.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var unsigned = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhookEndpoints.Path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await server.CreateClient().SendAsync(unsigned)).StatusCode);
        }

        Assert.Empty(queue.Envelopes);

        // The budget is untouched, so the first authenticated delivery fits and the next one in the
        // same window is refused.
        using var valid = SignedRequest(body);
        Assert.Equal(HttpStatusCode.OK, (await server.CreateClient().SendAsync(valid)).StatusCode);

        using var excess = SignedRequest(body);
        Assert.Equal((HttpStatusCode)429, (await server.CreateClient().SendAsync(excess)).StatusCode);
    }

    [Fact]
    public async Task Verification_get_is_not_subject_to_the_authenticated_delivery_budget()
    {
        using var server = Server(permitLimit: 1);
        var client = server.CreateClient();

        using var first = SignedRequest(TextPayload("wamid.verify-budget", "20100000012", "hello"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(first)).StatusCode);
        using var excess = SignedRequest(TextPayload("wamid.verify-budget-2", "20100000012", "hello"));
        Assert.Equal((HttpStatusCode)429, (await client.SendAsync(excess)).StatusCode);

        // Setup verification is what enables the webhook in the first place, so the exhausted delivery
        // budget of a live endpoint may not disable it.
        var verification = await client.GetAsync(
            $"{WhatsAppWebhookEndpoints.Path}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal("abc123", await verification.Content.ReadAsStringAsync());
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
                services.AddSingleton(new WhatsAppOptions
                {
                    ApiVersion = "v23.0",
                    PhoneNumberId = PhoneNumberId,
                    VerifyToken = VerifyToken,
                    AppSecret = AppSecret,
                    AccessToken = "access-token",
                    MaxWebhookBodyBytes = maxWebhookBodyBytes,
                    WebhookPermitLimit = permitLimit,
                    WebhookWindowSeconds = 60,
                });
                services.AddSingleton(provider =>
                    new WhatsAppWebhookDeliveryLimiter(provider.GetRequiredService<WhatsAppOptions>()));
                services.AddSingleton<IInboundMessageQueue>(queue);
            })
            .Configure(app =>
            {
                app.UseRouting();
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

    private static string TextMessage(string id, string from, string text) =>
        "{\"from\":\"" + from + "\",\"id\":\"" + id
        + "\",\"timestamp\":\"1700000000\",\"type\":\"text\",\"text\":{\"body\":\""
        + text + "\"}}";

    private static string TextPayload(string id, string from, string text) =>
        EntryWithValue(ValueWithMetadata(PhoneNumberId, TextMessage(id, from, text)));

    /// <summary>
    /// One Meta value as the provider builds it: the receiving business number in <c>metadata</c>
    /// alongside the messages it delivered.
    /// </summary>
    private static string ValueWithMetadata(string phoneNumberId, string messageJson) =>
        "{\"metadata\":{\"display_phone_number\":\"15550001111\",\"phone_number_id\":\""
        + phoneNumberId + "\"},\"messages\":[" + messageJson + "]}";

    private static string EntryWithValue(string valueJson) =>
        "{\"entry\":[{\"changes\":[{\"value\":" + valueJson + "}]}]}";

    private static string EntryWithChanges(params string[] valueJson) =>
        "{\"entry\":[{\"changes\":["
        + string.Join(",", valueJson.Select(value => "{\"value\":" + value + "}"))
        + "]}]}";

    private sealed class CapturingInboundQueue : IInboundMessageQueue
    {
        private readonly HashSet<string> acceptedProviderMessageIds = [];
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

            // The durable queue deduplicates by provider message id, so a repeated delivery of the same
            // message is a no-op that leaves one logical work item: it is acknowledged without another.
            if (!acceptedProviderMessageIds.Add(envelope.ProviderMessageId))
            {
                return Task.FromResult(new InboundEnqueueResult(Envelopes.Count, IsDuplicate: true));
            }

            Envelopes.Add(envelope);

            return Task.FromResult(new InboundEnqueueResult(Envelopes.Count, IsDuplicate: false));
        }
    }
}

#pragma warning restore ASPDEPR004, ASPDEPR008
