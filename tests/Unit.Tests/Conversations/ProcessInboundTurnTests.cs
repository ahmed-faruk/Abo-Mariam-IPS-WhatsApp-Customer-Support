using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The inbound orchestration of docs/TECHNICAL.md section 16: one accepted turn, one deterministic
/// decision, and at most one durable outbound intent whose correlation is the inbound message id.
/// </summary>
public sealed class ProcessInboundTurnTests
{
    [Fact]
    public async Task A_successful_turn_enqueues_exactly_one_reply_correlated_with_the_inbound_message()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));

        var result = await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.turn-42"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationMode.Ai, result.Mode);

        var request = Assert.Single(harness.Outbox.Requests);

        Assert.Equal("wamid.turn-42", request.CorrelationId);
        Assert.Equal(harness.Store.ConversationId, request.ConversationId);
        Assert.Equal("20100000001", request.CustomerExternalId);
        Assert.Equal("AI", request.Sender);
        Assert.Equal("the rendered reply", request.Body);
        Assert.Equal(harness.Outbox.NextMessageId, result.OutboxMessageId);

        // The lifecycle change is committed before the reply is stored, and the reply is recorded too.
        Assert.Equal(1, harness.Store.CommitCount);
        Assert.Equal(1, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task The_service_window_is_refreshed_from_the_provider_timestamp()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));
        var providerTimestamp = ConversationSamples.Now.AddMinutes(-30);

        await harness.ProcessAsync(ConversationSamples.Text(providerTimestamp: providerTimestamp));

        Assert.Equal(providerTimestamp, harness.Store.AcceptedProviderTimestamp);
        Assert.Equal(
            providerTimestamp.AddHours(24),
            harness.Store.WindowExpiresAt);
    }

    [Fact]
    public async Task A_closed_service_window_records_the_turn_and_enqueues_nothing()
    {
        // The inbound was sent more than 24 hours ago, so the window is already closed when the reply
        // would be enqueued.
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            providerTimestamp: ConversationSamples.Now.AddHours(-25)));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(1, harness.Store.CommitCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(0, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task A_window_that_expires_exactly_now_is_closed()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));
        var providerTimestamp = ConversationSamples.Now.AddHours(-24);

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerTimestamp: providerTimestamp));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task Human_mode_records_the_message_without_calling_the_model_or_the_renderer()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)),
            mode: ConversationModes.Human);

        var result = await harness.ProcessAsync(ConversationSamples.Text());

        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(1, harness.Store.CommitCount);
        Assert.NotNull(harness.Store.AcceptedAt);
    }

    [Fact]
    public async Task A_human_handoff_is_committed_with_the_turn_and_answered_with_its_own_intent()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        var result = await harness.ProcessAsync(ConversationSamples.Text(body: "عايز أكلم حد"));

        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(1, harness.Store.ModeChangeCount);
        Assert.Equal(ConversationResponseKind.HumanHandoff, Assert.Single(harness.Renderer.Intents).Kind);
    }

    [Fact]
    public async Task Unsupported_media_is_answered_without_calling_the_model()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(body: null, messageType: "image"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Equal(ConversationResponseKind.UnsupportedMedia, Assert.Single(harness.Renderer.Intents).Kind);
    }

    [Fact]
    public async Task An_empty_text_message_is_a_clarification_without_calling_the_model()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await harness.ProcessAsync(ConversationSamples.Text(body: "   "));

        Assert.Equal(0, harness.Nlu.CallCount);

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReasonCodes.EmptyMessage, intent.ReasonCode);
    }

    [Theory]
    [InlineData(NluAnalysisStatus.AiUnavailable)]
    [InlineData(NluAnalysisStatus.Timeout)]
    public async Task An_unavailable_or_slow_model_becomes_a_safe_unavailable_reply(NluAnalysisStatus status)
    {
        var harness = Harness.Start(
            status == NluAnalysisStatus.Timeout
                ? NluAnalysisResult.TimedOut()
                : NluAnalysisResult.AiUnavailable());

        await harness.ProcessAsync(ConversationSamples.Text());

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.AiUnavailable, intent.Kind);
        Assert.Equal(ConversationReasonCodes.AiUnavailable, intent.ReasonCode);
        Assert.Equal(ConversationStateDocument.Empty, harness.Store.State);
    }

    [Fact]
    public async Task An_invalid_model_reply_becomes_a_clarification_and_never_a_guess()
    {
        var harness = Harness.Start(NluAnalysisResult.InvalidModelOutput(["$.intent is not an allowed intent."]));

        await harness.ProcessAsync(ConversationSamples.Text());

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReasonCodes.InvalidModelOutput, intent.ReasonCode);
        Assert.Equal(ConversationStateDocument.Empty, harness.Store.State);
    }

    [Fact]
    public async Task An_unbound_renderer_produces_no_text_and_therefore_no_durable_reply()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)),
            renderedBody: null);

        var result = await harness.ProcessAsync(ConversationSamples.Text());

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Null(result.OutboxMessageId);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(1, harness.Store.CommitCount);
    }

    [Fact]
    public async Task A_failing_outbox_fails_the_turn_so_the_durable_inbox_retries_it()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));
        harness.Outbox.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(ConversationSamples.Text()));

        // The Conversations change is already committed, which is exactly what makes the retry safe.
        Assert.Equal(1, harness.Store.CommitCount);
    }

    [Fact]
    public async Task The_known_conversation_of_the_envelope_is_passed_to_the_store()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await harness.ProcessAsync(ConversationSamples.Text(conversationId: 4242));

        Assert.Equal(4242, harness.Store.OpenedKnownConversationId);
    }

    [Fact]
    public async Task The_turn_records_the_customer_it_belongs_to()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await harness.ProcessAsync(ConversationSamples.Text(customerExternalId: "20100009999"));

        Assert.Equal("20100009999", harness.Store.OpenedCustomerExternalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_turn_without_a_provider_message_id_is_rejected(string? providerMessageId)
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await Assert.ThrowsAsync<ArgumentException>(() => harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: providerMessageId!)));
    }

    [Fact]
    public async Task An_empty_message_body_is_only_ignored_for_text_messages()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)));

        await harness.ProcessAsync(ConversationSamples.Text(body: null, messageType: "audio"));

        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Equal(ConversationResponseKind.UnsupportedMedia, Assert.Single(harness.Renderer.Intents).Kind);
    }

    /// <summary>One orchestration under test, wired the way the host wires it.</summary>
    private sealed class Harness
    {
        private Harness(
            FakeConversationTurnStore store,
            FakeAiNluClient nlu,
            FakeConversationRenderer renderer,
            RecordingOutboundMessageQueue outbox,
            ProcessInboundTurnHandler handler)
        {
            Store = store;
            Nlu = nlu;
            Renderer = renderer;
            Outbox = outbox;
            Handler = handler;
        }

        internal FakeConversationTurnStore Store { get; }

        internal FakeAiNluClient Nlu { get; }

        internal FakeConversationRenderer Renderer { get; }

        internal RecordingOutboundMessageQueue Outbox { get; }

        internal ProcessInboundTurnHandler Handler { get; }

        internal static Harness Start(
            NluAnalysisResult analysis,
            string mode = ConversationModes.Ai,
            string? renderedBody = "the rendered reply")
        {
            var store = new FakeConversationTurnStore { Mode = mode };
            var nlu = new FakeAiNluClient { Answer = _ => analysis };
            var renderer = new FakeConversationRenderer { Body = renderedBody };
            var outbox = new RecordingOutboundMessageQueue();
            var router = new ConversationIntentRouter(new FakeCatalogSearch(), new FakeCatalogProductDetails());
            var clock = new TestClock(ConversationSamples.Now);

            return new Harness(
                store,
                nlu,
                renderer,
                outbox,
                new ProcessInboundTurnHandler(store, router, nlu, renderer, outbox, clock));
        }

        internal Task<ConversationTurnResult> ProcessAsync(InboundTurn turn) =>
            Handler.ProcessAsync(turn);
    }
}
