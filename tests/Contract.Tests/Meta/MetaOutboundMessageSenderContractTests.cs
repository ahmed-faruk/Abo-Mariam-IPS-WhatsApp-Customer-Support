using System.Net;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Meta;

namespace WhatsAppMonitorAssistant.Contract.Tests.Meta;

public sealed class MetaOutboundMessageSenderContractTests
{
    [Fact]
    public async Task Accepted_response_sends_the_documented_text_request_and_returns_provider_id()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"messages":[{"id":"wamid.sent"}]}"""),
        });
        var sender = Sender(handler);

        var result = await sender.SendAsync(Message(body: "hello"));

        Assert.Equal(OutboundSendOutcome.Accepted, result.Outcome);
        Assert.Equal("wamid.sent", result.ProviderMessageId);
        Assert.Equal("https://graph.facebook.com/v23.0/123/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("access-token", handler.Request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("DeliveryKey", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("outbox:", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"messaging_product\":\"whatsapp\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"to\":\"20100000001\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"body\":\"hello\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Success_without_provider_id_is_unknown()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"messages":[]}"""),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Too_many_requests_is_retryable_and_preserves_retry_after()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"error":{"code":130429,"message":"rate limit"}}"""),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
        var sender = Sender(new CapturingHandler(response));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(17), result.RetryAfter);
    }

    [Fact]
    public async Task Invalid_request_error_is_permanent()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json("""{"error":{"code":100,"message":"invalid parameter"}}"""),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.PermanentFailure, result.Outcome);
    }

    [Fact]
    public async Task Server_error_without_provider_evidence_is_unknown()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = Json("""{"error":{"message":"oops"}}"""),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Timeout_is_unknown()
    {
        var sender = Sender(new CapturingHandler(new TaskCanceledException("request timed out")));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Over_provider_text_limit_is_permanent_without_http_request()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sender = Sender(handler);

        var result = await sender.SendAsync(Message(body: new string('x', WhatsAppOptions.MaxTextBodyLength + 1)));

        Assert.Equal(OutboundSendOutcome.PermanentFailure, result.Outcome);
        Assert.Null(handler.Request);
    }

    private static MetaOutboundMessageSender Sender(CapturingHandler handler) =>
        new(
            new HttpClient(handler),
            new WhatsAppOptions
            {
                ApiVersion = "v23.0",
                PhoneNumberId = "123",
                AccessToken = "access-token",
                AppSecret = "secret",
                VerifyToken = "verify",
            });

    private static ClaimedOutboxMessage Message(string body = "reply") =>
        new(
            Id: 42,
            ConversationId: 7,
            CustomerExternalId: "20100000001",
            CorrelationId: "wamid.inbound",
            Sender: "AI",
            Body: body,
            ProviderMessageId: null,
            Attempts: 1,
            MaxAttempts: 5,
            DeliveryKey: "outbox:42",
            ClaimToken: Guid.NewGuid());

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? response;
        private readonly Exception? exception;

        public CapturingHandler(HttpResponseMessage response) => this.response = response;

        public CapturingHandler(Exception exception) => this.exception = exception;

        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            if (exception is not null)
            {
                throw exception;
            }

            return response!;
        }
    }
}
