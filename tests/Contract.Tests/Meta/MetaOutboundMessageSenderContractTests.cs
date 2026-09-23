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
    public async Task A_stalled_response_body_ends_at_the_configured_attempt_timeout()
    {
        var sender = Sender(
            new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() }),
            timeoutSeconds: 1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await sender.SendAsync(Message()).WaitAsync(TimeSpan.FromSeconds(30));

        stopwatch.Stop();

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
        // The expected diagnostic is the stable contract value, kept independent of the production
        // constant: a regression that renamed or reworded the diagnostic must fail this test.
        Assert.Equal("MetaUnknownTimeout", result.Error);
        Assert.Null(result.ProviderMessageId);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"The attempt lasted {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_a_provider_outcome()
    {
        var sender = Sender(
            new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() }),
            timeoutSeconds: 30);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(Message(), cancellation.Token));
    }

    [Fact]
    public async Task The_complete_success_body_is_parsed_past_the_former_truncation_bound()
    {
        var providerMessageId = "wamid.complete-body";
        var padded = new string('p', 4_000);
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(
                "{\"messages\":[{\"id\":\"" + providerMessageId + "\"}],\"padding\":\"" + padded + "\"}"),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Accepted, result.Outcome);
        Assert.Equal(providerMessageId, result.ProviderMessageId);
    }

    [Fact]
    public async Task A_response_beyond_the_read_bound_is_not_parsed_and_fabricates_no_provider_id()
    {
        var oversized = "{\"messages\":[{\"id\":\"wamid.too-large\"}],\"padding\":\""
            + new string('p', 200_000) + "\"}";
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(oversized),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
        Assert.Equal("MetaResponseTooLarge", result.Error);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task A_malformed_success_body_is_unknown_without_a_provider_id()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"messages\":[{\"id\":"),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
        Assert.Equal("MetaMalformedSuccessResponse", result.Error);
        Assert.Null(result.ProviderMessageId);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"OK\"")]
    [InlineData("17")]
    public async Task A_success_body_that_is_not_an_object_is_unknown_without_a_provider_id(string body)
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(body),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
        Assert.Equal("MetaMalformedSuccessResponse", result.Error);
        Assert.Null(result.ProviderMessageId);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    public async Task A_non_object_error_body_is_classified_conservatively(string body)
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json(body),
        }));

        var result = await sender.SendAsync(Message());

        // The status is a known rejection, but the unreadable body proves nothing about permanence.
        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
    }

    [Fact]
    public async Task A_server_error_is_never_reported_as_a_permanent_refusal()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = Json("""{"error":{"code":131047,"message":"re-engagement"}}"""),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
    }

    [Theory]
    [InlineData(130429)]
    [InlineData(131000)]
    [InlineData(131057)]
    [InlineData(131047)]
    [InlineData(999999)]
    public async Task A_server_error_stays_unknown_whatever_provider_code_its_body_carries(int code)
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = Json("{\"error\":{\"code\":" + code + ",\"message\":\"provider text\"}}"),
        }));

        var result = await sender.SendAsync(Message());

        // A 5xx cannot prove that Meta refused the request, so acceptance stays uncertain and the
        // attempt is never described as a known unsuccessful outcome on the strength of a body code.
        Assert.Equal(OutboundSendOutcome.Unknown, result.Outcome);
    }

    [Theory]
    [InlineData(131000)]
    [InlineData(131057)]
    public async Task documented_temporary_provider_errors_are_retryable_even_without_http_429(int code)
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json("{\"error\":{\"code\":" + code + ",\"message\":\"temporary failure\"}}"),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
        Assert.Contains($"code={code}", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unrecognised_provider_error_code_is_not_treated_as_permanent()
    {
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json("""{"error":{"code":999999,"message":"something new"}}"""),
        }));

        var result = await sender.SendAsync(Message());

        Assert.NotEqual(OutboundSendOutcome.PermanentFailure, result.Outcome);
        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
    }

    [Fact]
    public async Task Provider_free_text_never_reaches_the_persisted_diagnostic()
    {
        var rawText = "customer 20100000001 asked about " + new string('x', 50_000);
        var sender = Sender(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json(
                "{\"error\":{\"code\":131047,\"message\":\"" + rawText + "\"}}"),
        }));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.PermanentFailure, result.Outcome);
        Assert.Equal("MetaPermanentFailure status=400 code=131047", result.Error);
        Assert.DoesNotContain("20100000001", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxx", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_excessive_delta_retry_hint_never_delays_the_attempt_and_stays_positive()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"error":{"code":130429,"message":"rate limit"}}"""),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromDays(3650));
        var sender = Sender(new CapturingHandler(response));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await sender.SendAsync(Message());

        stopwatch.Stop();

        // The adapter relays the hint and returns; bounding the durable schedule belongs to the queue.
        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter!.Value > TimeSpan.Zero);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The attempt lasted {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_far_future_http_date_retry_hint_never_delays_the_attempt_and_stays_positive()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"error":{"code":130429,"message":"rate limit"}}"""),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sender = Sender(new CapturingHandler(response));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await sender.SendAsync(Message());

        stopwatch.Stop();

        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter!.Value > TimeSpan.Zero);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The attempt lasted {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_reasonable_http_date_retry_hint_is_preserved()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"error":{"code":130429,"message":"rate limit"}}"""),
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddSeconds(45));
        var sender = Sender(new CapturingHandler(response));

        var result = await sender.SendAsync(Message());

        Assert.NotNull(result.RetryAfter);
        Assert.InRange(result.RetryAfter!.Value, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_malformed_retry_hint_is_ignored()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"error":{"code":130429,"message":"rate limit"}}"""),
        };
        response.Headers.TryAddWithoutValidation("Retry-After", "not-a-delay");
        var sender = Sender(new CapturingHandler(response));

        var result = await sender.SendAsync(Message());

        Assert.Equal(OutboundSendOutcome.RetryableFailure, result.Outcome);
        Assert.Null(result.RetryAfter);
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

    private static MetaOutboundMessageSender Sender(CapturingHandler handler, int timeoutSeconds = 20) =>
        new(
            new HttpClient(handler),
            new WhatsAppOptions
            {
                ApiVersion = "v23.0",
                PhoneNumberId = "123",
                AccessToken = "access-token",
                AppSecret = "secret",
                VerifyToken = "verify",
                TimeoutSeconds = timeoutSeconds,
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

    /// <summary>
    /// A response whose headers arrive immediately while the body never finishes. The stall is
    /// observed through the cancellation token the reader passes to the stream, so the test proves
    /// the attempt timeout covers the body and not only the headers.
    /// </summary>
    private sealed class StallingContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new StallingStream());

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.Delay(Timeout.Infinite, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }

        private sealed class StallingStream : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override async Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);

                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }

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
