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
    /// <summary>
    /// The identifiers these tests expect are stated here, independently of the fakes that hand the
    /// same values to the production code, so a fake that returns a wrong id cannot agree with itself.
    /// </summary>
    private const long KnownConversationId = 4242;

    private const long KnownOutboxMessageId = 5017;

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
        Assert.Equal(KnownConversationId, request.ConversationId);
        Assert.Equal("20100000001", request.CustomerExternalId);
        Assert.Equal("AI", request.Sender);
        Assert.Equal("the rendered reply", request.Body);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);

        // The lifecycle change is committed before the reply is stored, the reply is recorded, and the
        // authorized commit releases the final-operation lock.
        Assert.Equal(2, harness.Store.CommitCount);
        Assert.Equal(1, harness.Store.OutboundRecordCount);
        Assert.True(
            harness.Journal.IndexOf("store:commit") < harness.Journal.IndexOf("outbox:enqueue"),
            string.Join(", ", harness.Journal));
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
    public async Task A_handoff_becomes_human_only_after_the_outbox_accepted_the_acknowledgement()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(body: "عايز أكلم حد", providerMessageId: "wamid.handoff-order"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);

        // The acknowledgement is durable before the mode change is committed, which is what makes a
        // crash between the two safe: the Inbox retries, reuses the same Outbox row, and only then
        // completes the handoff.
        Assert.True(
            harness.Journal.IndexOf("outbox:enqueue") < harness.Journal.IndexOf("store:set-mode:Human"),
            string.Join(", ", harness.Journal));
    }

    [Fact]
    public async Task A_handoff_whose_acknowledgement_cannot_be_stored_stays_automatic_so_the_inbox_retries_it()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        harness.Outbox.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(
            ConversationSamples.Text(body: "عايز أكلم حد", providerMessageId: "wamid.handoff-failed")));

        // Nothing may record the conversation as human while its acknowledgement was never stored:
        // the retried turn has to try again instead of short-circuiting on a durable Human mode.
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
    }

    [Fact]
    public async Task A_retried_handoff_stores_the_acknowledgement_again_and_then_enters_human()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        var turn = ConversationSamples.Text(body: "عايز أكلم حد", providerMessageId: "wamid.handoff-retry");

        harness.Outbox.Fails = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(turn));

        harness.Outbox.Fails = false;
        var retried = await harness.ProcessAsync(turn);

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, retried.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);

        // Both attempts correlate the acknowledgement with the same inbound turn, so the durable
        // Outbox keeps reusing its original row instead of storing a second reply.
        Assert.Equal(2, harness.Outbox.Requests.Count);
        Assert.All(
            harness.Outbox.Requests,
            request => Assert.Equal("wamid.handoff-retry", request.CorrelationId));
    }

    [Fact]
    public async Task A_handoff_that_an_operator_already_took_over_is_not_answered()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        // The operator takes over while the turn is still interpreting the message, so the takeover
        // has committed Human before the turn asks for its final authorization.
        harness.Nlu.Answer = _ =>
        {
            harness.Store.Mode = ConversationModes.Human;

            return NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.HumanHandoff));
        };

        var result = await harness.ProcessAsync(ConversationSamples.Text(body: "عايز أكلم حد"));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(0, harness.Store.ModeChangeCount);
    }

    [Fact]
    public async Task A_handoff_that_lost_the_race_to_a_close_never_reopens_the_conversation()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        harness.Nlu.Answer = _ =>
        {
            harness.Store.Mode = ConversationModes.Closed;

            return NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.HumanHandoff));
        };

        var result = await harness.ProcessAsync(ConversationSamples.Text(body: "عايز أكلم حد"));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Closed, result.Mode);
        Assert.Equal(ConversationModes.Closed, harness.Store.Mode);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task An_automatic_turn_that_was_closed_while_it_interpreted_is_not_answered()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));

        harness.Nlu.Answer = _ =>
        {
            harness.Store.Mode = ConversationModes.Closed;

            return NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting));
        };

        var result = await harness.ProcessAsync(ConversationSamples.Text());

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Closed, result.Mode);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(0, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task A_handoff_whose_renderer_produces_nothing_still_enters_human()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.HumanHandoff)),
            renderedBody: null);

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(body: "عايز أكلم حد", providerMessageId: "wamid.handoff-unbound"));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_handoff_whose_service_window_is_closed_still_enters_human_without_a_reply()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            body: "عايز أكلم حد",
            providerTimestamp: ConversationSamples.Now.AddHours(-25)));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
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
    public async Task A_search_shortlist_becomes_addressable_once_the_outbox_accepted_the_reply()
    {
        var harness = StartSearchHarness();
        harness.Search.Results =
        [
            ConversationSamples.Recommendation(10, 21),
            ConversationSamples.Recommendation(11, 25),
        ];

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.displayed-1"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Equal(21, harness.Store.State.LastVariantId);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("closed-window")]
    [InlineData("failed-outbox")]
    public async Task A_search_shortlist_that_was_never_shown_never_becomes_addressable(string scenario)
    {
        var harness = StartSearchHarness(renderedBody: scenario == "unbound" ? null : "the rendered reply");
        harness.Store.State = PreviouslyShown((5, 51), (6, 61));
        harness.Search.Results =
        [
            ConversationSamples.Recommendation(10, 21),
            ConversationSamples.Recommendation(11, 25),
        ];

        if (scenario == "failed-outbox")
        {
            harness.Outbox.Fails = true;
        }

        var turn = ConversationSamples.Text(
            providerMessageId: $"wamid.unseen-{scenario}",
            providerTimestamp: scenario == "closed-window" ? ConversationSamples.Now.AddHours(-25) : null);

        if (scenario == "failed-outbox")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(turn));
        }
        else
        {
            var result = await harness.ProcessAsync(turn);

            Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        }

        // No reply of this turn was accepted, so no outbound was ever recorded for it.
        Assert.Null(harness.Store.LastOutboundRecordedAt);

        // The customer was never shown the new list, so the previously confirmed list and its current
        // product stay exactly as they were and no unshown product became the conversation's reference.
        Assert.Equal([5, 6], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Equal(51, harness.Store.State.LastVariantId);
        Assert.NotEqual(10, harness.Store.State.LastModelId);
    }

    [Fact]
    public async Task The_effective_filters_of_a_search_survive_a_turn_that_could_not_be_answered()
    {
        var harness = StartSearchHarness(renderedBody: null);
        harness.Nlu.Answer = _ => NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch, brand: "Dell", sizeInches: 24));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.filters-1"));

        harness.Nlu.Answer = _ => NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch, panel: "IPS"));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.filters-2"));

        var query = harness.Search.Queries[^1];

        Assert.Equal("Dell", query.Brand);
        Assert.Equal(24, query.SizeInches);
        Assert.Equal("IPS", query.PanelType);
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

        await harness.ProcessAsync(ConversationSamples.Text(conversationId: 9901));

        Assert.Equal(9901, harness.Store.OpenedKnownConversationId);
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

    [Fact]
    public async Task A_follow_up_search_refines_the_search_the_previous_turn_stored()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch, brand: "Dell", sizeInches: 24)));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.refine-1"));

        harness.Nlu.Answer = _ => NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch, panel: "IPS", requiredPorts: ["HDMI"]));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.refine-2"));

        var query = harness.Search.Queries[^1];

        Assert.Equal("Dell", query.Brand);
        Assert.Equal(24, query.SizeInches);
        Assert.Equal("IPS", query.PanelType);
        Assert.Equal(["HDMI"], query.RequiredPorts);
    }

    [Fact]
    public async Task A_comparison_after_a_candidate_left_the_catalogue_is_a_no_match()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch)));
        harness.Search.Results =
        [
            ConversationSamples.Recommendation(10, 21),
            ConversationSamples.Recommendation(11, 25),
        ];
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21));
        harness.Details.Publish(ConversationSamples.ActiveModel(11, 25));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.compare-1"));

        harness.Details.Withdraw(11);
        harness.Nlu.Answer = _ => NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductComparison));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.compare-2"));

        var intent = harness.Renderer.Intents[^1];

        Assert.Equal(ConversationResponseKind.NoMatch, intent.Kind);
        Assert.Equal(ConversationReasonCodes.ProductNoLongerAvailable, intent.ReasonCode);
        Assert.DoesNotContain(
            ConversationResponseKind.ProductComparison,
            harness.Renderer.Intents.Select(recorded => recorded.Kind));
    }

    [Fact]
    public async Task A_reference_to_a_model_retired_after_the_search_is_a_no_match()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch)));
        harness.Search.Results = [ConversationSamples.Recommendation(10, 21)];
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.retired-1"));

        harness.Details.Publish(ConversationSamples.Model(
            10,
            isActive: false,
            ConversationSamples.Variant(21, isActive: true, quantity: 3)));
        harness.Nlu.Answer = _ => NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: "first"));

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.retired-2"));

        var intent = harness.Renderer.Intents[^1];

        Assert.Equal(ConversationResponseKind.NoMatch, intent.Kind);
        Assert.Equal(ConversationReasonCodes.ProductNoLongerAvailable, intent.ReasonCode);
        Assert.DoesNotContain(ConversationResponseKind.Price, harness.Renderer.Intents.Select(recorded => recorded.Kind));
    }

    [Fact]
    public async Task A_structurally_malformed_stored_state_never_breaks_the_turn()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: "first")));
        harness.Store.StoredStateJson = """{"shortlist":null}""";

        var result = await harness.ProcessAsync(ConversationSamples.Text());

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, intent.ReasonCode);
    }

    [Fact]
    public async Task A_persisted_current_reference_with_a_non_positive_id_is_never_used_as_the_current_product()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: "this")));
        harness.Store.StoredStateJson = """{"lastModelId":0,"lastVariantId":-1}""";

        var result = await harness.ProcessAsync(ConversationSamples.Text());

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReferenceReasons.CurrentReferenceMissing, intent.ReasonCode);
        Assert.DoesNotContain(ConversationResponseKind.Price, harness.Renderer.Intents.Select(recorded => recorded.Kind));
    }

    [Fact]
    public async Task An_ambiguous_business_info_question_asks_instead_of_answering_one_concept()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.BusinessInfo)));

        await harness.ProcessAsync(ConversationSamples.Text(body: "مواعيدكم إيه والعنوان فين؟"));

        var intent = Assert.Single(harness.Renderer.Intents);

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReasonCodes.BusinessInfoKeyAmbiguous, intent.ReasonCode);
        Assert.Null(intent.StorefrontKey);
    }

    /// <summary>The orchestration of a product search, wired the way the host wires it.</summary>
    private static Harness StartSearchHarness(string? renderedBody = "the rendered reply") =>
        Harness.Start(
            NluAnalysisResult.Success(
                ConversationSamples.Interpretation(NluIntent.ProductSearch, brand: "Dell")),
            renderedBody: renderedBody);

    /// <summary>The UX state of a list the customer has already been shown.</summary>
    private static ConversationStateDocument PreviouslyShown(
        params (long ModelId, long VariantId)[] candidates) =>
        ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist(candidates),
            LastModelId = candidates[0].ModelId,
            LastVariantId = candidates[0].VariantId,
        };

    /// <summary>One orchestration under test, wired the way the host wires it.</summary>
    private sealed class Harness
    {
        private Harness(
            FakeConversationTurnStore store,
            FakeAiNluClient nlu,
            FakeConversationRenderer renderer,
            RecordingOutboundMessageQueue outbox,
            FakeCatalogSearch search,
            FakeCatalogProductDetails details,
            ProcessInboundTurnHandler handler,
            List<string> journal)
        {
            Store = store;
            Nlu = nlu;
            Renderer = renderer;
            Outbox = outbox;
            Search = search;
            Details = details;
            Handler = handler;
            Journal = journal;
        }

        internal FakeConversationTurnStore Store { get; }

        internal FakeAiNluClient Nlu { get; }

        internal FakeConversationRenderer Renderer { get; }

        internal RecordingOutboundMessageQueue Outbox { get; }

        internal FakeCatalogSearch Search { get; }

        internal FakeCatalogProductDetails Details { get; }

        internal ProcessInboundTurnHandler Handler { get; }

        /// <summary>The observable order the turn touched its fakes.</summary>
        internal List<string> Journal { get; }

        internal static Harness Start(
            NluAnalysisResult analysis,
            string mode = ConversationModes.Ai,
            string? renderedBody = "the rendered reply")
        {
            var journal = new List<string>();
            var store = new FakeConversationTurnStore
            {
                Mode = mode,
                ConversationId = KnownConversationId,
                Journal = journal,
            };
            var nlu = new FakeAiNluClient { Answer = _ => analysis };
            var renderer = new FakeConversationRenderer { Body = renderedBody, Journal = journal };
            var outbox = new RecordingOutboundMessageQueue
            {
                NextMessageId = KnownOutboxMessageId,
                Journal = journal,
            };
            var search = new FakeCatalogSearch();
            var details = new FakeCatalogProductDetails();
            var router = new ConversationIntentRouter(search, details);
            var clock = new TestClock(ConversationSamples.Now);

            return new Harness(
                store,
                nlu,
                renderer,
                outbox,
                search,
                details,
                new ProcessInboundTurnHandler(store, router, nlu, renderer, outbox, clock),
                journal);
        }

        internal Task<ConversationTurnResult> ProcessAsync(InboundTurn turn) =>
            Handler.ProcessAsync(turn);
    }
}
