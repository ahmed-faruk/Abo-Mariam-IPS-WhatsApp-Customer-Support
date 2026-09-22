using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

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

        // The turn's own context is committed first, and the final-operation section is committed when it
        // finds that there is no durable reply to reconcile and that a new one may not be sent.
        Assert.Equal(2, harness.Store.CommitCount);
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

        // The turn's own context is committed first, and the final-operation section is committed when it
        // decides that nothing may be sent.
        Assert.Equal(2, harness.Store.CommitCount);
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
        harness.DisplayWhatTheSearchReturns();

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.displayed-1"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Equal(21, harness.Store.State.LastVariantId);

        // The list that became addressable is the one the renderer said it displayed, and the durable row
        // carries exactly that order as its opaque metadata.
        var metadata = ConversationOutboxMetadata.Parse(
            Assert.Single(harness.Outbox.Requests).ApplicationMetadata);

        Assert.NotNull(metadata);
        Assert.Equal(ConversationOutboxMetadata.CurrentVersion, metadata.Version);
        Assert.False(metadata.EntersHumanMode);
        Assert.Equal(
            [(10L, 21L), (11L, 25L)],
            metadata.DisplayedCandidates.Select(candidate => (candidate.ModelId, candidate.VariantId)));
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
        harness.DisplayWhatTheSearchReturns();

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
    public async Task A_search_reply_that_displayed_nothing_leaves_the_previously_shown_list_alone()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Search.Results = [ConversationSamples.Recommendation(10, 21)];

        // The renderer answers the search with text but displays no product: nothing may be published.
        harness.Renderer.DisplayedCandidates = _ => [];

        var result = await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.none"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Equal(51, harness.Store.State.LastVariantId);
    }

    [Fact]
    public async Task A_retried_turn_reconciles_the_reply_it_already_stored_instead_of_rendering_again()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));
        var turn = ConversationSamples.Text(providerMessageId: "wamid.replay");

        var first = await harness.ProcessAsync(turn);

        harness.Renderer.Body = null;
        var retried = await harness.ProcessAsync(turn);

        // The earlier attempt already accepted this turn's reply, so the retry reuses that immutable row
        // and never asks the renderer for a second, possibly different, reply.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, retried.Outcome);
        Assert.Equal(first.OutboxMessageId, retried.OutboxMessageId);
        Assert.Single(harness.Renderer.Intents);
        Assert.Single(harness.Outbox.Requests);

        // Replaying the same accepted reply records the same outbound lifecycle again; it stores no second
        // reply and republishes nothing new.
        Assert.Equal(2, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task A_retried_turn_republishes_the_list_its_stored_reply_really_showed()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));

        // An earlier attempt of this turn stored a reply that displayed these two products and then died
        // before its Conversations change was committed.
        harness.Outbox.PreStore(
            "wamid.replay-display",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.replay-display"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Equal(21, harness.Store.State.LastVariantId);
    }

    [Fact]
    public async Task A_retried_handoff_completes_from_the_effect_its_stored_reply_recorded()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));

        harness.Outbox.PreStore(
            "wamid.replay-handoff",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.replay-handoff"));

        // The stored reply was the handoff acknowledgement, so the retry completes the mode change the
        // earlier attempt never committed - even though this attempt's own decision was a greeting.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Empty(harness.Renderer.Intents);
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_is_reused_but_claims_no_displayed_list()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore("wamid.legacy", "an older reply");

        var result = await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.legacy"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_never_infers_its_handoff_from_the_retried_route()
    {
        // The current attempt decides this message is a handoff, but the immutable row it finds was stored
        // before metadata existed and carries no evidence that its own body was ever a handoff. The current
        // route is not proof of what that older reply meant, so the conversation must stay automatic.
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        harness.Outbox.PreStore("wamid.legacy-handoff", "an older reply");

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            body: "عايز أكلم حد",
            providerMessageId: "wamid.legacy-handoff"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_leaves_an_operators_human_mode_alone()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)),
            mode: ConversationModes.Human);
        harness.Outbox.PreStore("wamid.legacy-human", "an older reply");

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.legacy-human"));

        // The durable row of this correlation is reused and its lifecycle recorded, but a payload-less
        // reply proves nothing about the outcome it carried, so the mode the operator chose stays as it is
        // and nothing new is interpreted, rendered or stored.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_leaves_a_closed_conversation_closed()
    {
        var harness = Harness.Start(
            NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting)),
            mode: ConversationModes.Closed);
        harness.Outbox.PreStore("wamid.legacy-closed", "an older reply");

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.legacy-closed"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationModes.Closed, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_reply_this_turn_already_stored_is_reconciled_even_after_the_window_closed()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));

        // An earlier attempt of this turn stored its reply - a list of two products - and then died before
        // its Conversations change was committed.
        harness.Outbox.PreStore(
            "wamid.replay-closed",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            providerMessageId: "wamid.replay-closed",
            providerTimestamp: ConversationSamples.Now.AddHours(-25)));

        // The reply was durably accepted while the application was still allowed to create one, so the
        // closed window may not make the retry forget it: it is reconciled and never rendered again.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Equal(21, harness.Store.State.LastVariantId);
        Assert.NotNull(harness.Store.LastOutboundRecordedAt);
    }

    [Fact]
    public async Task A_reply_this_turn_already_stored_is_reconciled_even_after_an_operator_took_over()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore(
            "wamid.replay-taken",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());

        // The operator takes the conversation over while this turn is being prepared.
        harness.Nlu.Answer = _ =>
        {
            harness.Store.Mode = ConversationModes.Human;

            return NluAnalysisResult.Success(
                ConversationSamples.Interpretation(NluIntent.ProductSearch, brand: "Dell"));
        };

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.replay-taken"));

        // The new reply is suppressed, but the reply this turn already durably accepted is still
        // reconciled: the customer was sent it, so its displayed list becomes addressable.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
    }

    [Fact]
    public async Task A_stored_handoff_reply_never_reopens_a_conversation_an_operator_closed()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));
        harness.Outbox.PreStore(
            "wamid.replay-closed-mode",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        // The operator closes the conversation while this turn is being prepared, after the earlier attempt
        // had already stored the acknowledgement.
        harness.Nlu.Answer = _ =>
        {
            harness.Store.Mode = ConversationModes.Closed;

            return NluAnalysisResult.Success(ConversationSamples.Interpretation(NluIntent.Greeting));
        };

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.replay-closed-mode"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationModes.Closed, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_stored_reply_of_an_unknown_metadata_version_fails_the_turn_instead_of_guessing()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));
        harness.Outbox.PreStore("wamid.future-metadata", "a reply", """{"v":99}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.future-metadata")));

        Assert.Empty(harness.Renderer.Intents);
    }

    [Theory]
    [InlineData(ConversationModes.Human)]
    [InlineData(ConversationModes.Closed)]
    public async Task A_stored_search_reply_is_reconciled_even_when_the_retry_starts_non_automatic(string mode)
    {
        // The conversation is no longer automatic when the retry is accepted, because the operator took it
        // over or closed it after the earlier attempt had already stored this turn's reply.
        var harness = StartSearchHarness();
        harness.Store.Mode = mode;
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore(
            "wamid.non-automatic-retry",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.non-automatic-retry"));

        // The stored reply was already accepted, so it is reconciled: no interpretation, no renderer, no
        // second reply, and the list the customer really received becomes addressable.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(mode, harness.Store.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Equal(21, harness.Store.State.LastVariantId);
        Assert.NotNull(harness.Store.LastOutboundRecordedAt);
    }

    [Theory]
    [InlineData(ConversationModes.Human)]
    [InlineData(ConversationModes.Closed)]
    public async Task A_stored_handoff_reply_is_reconciled_without_overriding_a_non_automatic_mode(string mode)
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        harness.Store.Mode = mode;
        harness.Outbox.PreStore(
            "wamid.non-automatic-handoff",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            body: "عايز أكلم حد",
            providerMessageId: "wamid.non-automatic-handoff"));

        // A stored handoff may move an automatic conversation to Human, and nothing else: it never reopens
        // a closed conversation and never releases a human one.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(mode, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Theory]
    [InlineData(ConversationModes.Human)]
    [InlineData(ConversationModes.Closed)]
    public async Task A_stored_reply_without_metadata_leaves_a_non_automatic_mode_and_its_list_alone(string mode)
    {
        var harness = StartSearchHarness();
        harness.Store.Mode = mode;
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore("wamid.non-automatic-legacy", "an older reply");

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.non-automatic-legacy"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(mode, harness.Store.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Equal(51, harness.Store.State.LastVariantId);
    }

    [Fact]
    public async Task A_reply_accepted_by_another_conversation_is_never_reconciled_onto_this_one()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));

        // The durable reply of this correlation belongs to the conversation an earlier attempt answered,
        // not to the one this retry resolves to: the operator closed that conversation and this turn opened
        // a new active one. Nothing of that reply may land on the new conversation.
        harness.Outbox.PreStore(
            "wamid.foreign-reply",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson(),
            conversationId: KnownConversationId + 1);

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.foreign-reply"));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Null(result.OutboxMessageId);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Null(harness.Store.LastOutboundRecordedAt);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);

        // The old inbound of another conversation is not recorded here either: no lifecycle, no commit, and
        // no service window refreshed from its provider timestamp.
        Assert.Null(harness.Store.AcceptedAt);
        Assert.Equal(0, harness.Store.CommitCount);
    }

    [Fact]
    public async Task A_reply_accepted_by_another_conversation_is_not_reconciled_by_a_human_conversation_either()
    {
        var harness = StartSearchHarness();
        harness.Store.Mode = ConversationModes.Human;
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore(
            "wamid.foreign-human-reply",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: false).ToJson(),
            conversationId: KnownConversationId + 1);

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.foreign-human-reply"));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Null(result.OutboxMessageId);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Null(harness.Store.LastOutboundRecordedAt);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Null(harness.Store.AcceptedAt);
        Assert.Equal(0, harness.Store.CommitCount);
    }

    [Fact]
    public async Task An_enqueue_that_loses_to_a_reply_of_another_conversation_fails_the_turn_loudly()
    {
        var harness = StartSearchHarness();
        harness.Store.State = PreviouslyShown((5, 51));

        // The correlation was absent when this turn read it, and the row that won it belongs to another
        // conversation. That reply may not be reconciled onto this one and no second reply may be created
        // for it, so the turn fails loudly instead of quietly adopting somebody else's reply.
        harness.Outbox.ConflictingAcceptance = new OutboundAcceptance(
            KnownOutboxMessageId,
            KnownConversationId + 1,
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson(),
            IsExisting: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.conflicting-enqueue")));

        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Null(harness.Store.LastOutboundRecordedAt);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Equal(51, harness.Store.State.LastVariantId);
    }

    [Fact]
    public async Task A_brand_new_inbound_of_a_human_conversation_still_reaches_nothing_but_the_record()
    {
        // Nothing of this inbound is durable, so a human conversation keeps the Issue #11 behaviour
        // exactly: the message is recorded, and no interpretation, renderer or reply follows it.
        var harness = StartSearchHarness();
        harness.Store.Mode = ConversationModes.Human;

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.human-new-inbound"));

        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(1, harness.Store.CommitCount);
        Assert.Equal(0, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task A_brand_new_inbound_of_a_human_conversation_still_awaits_a_human_after_a_release()
    {
        var harness = StartSearchHarness();
        harness.Store.Mode = ConversationModes.Human;

        // The operator releases the conversation to the assistant before this inbound's final section.
        harness.Store.OnBeginFinalOperation = () => harness.Store.Mode = ConversationModes.Ai;

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.human-released-new"));

        // The inbound was accepted by a conversation a human owned, so it is not interpreted, rendered or
        // answered by this turn whatever the mode becomes meanwhile.
        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, result.Outcome);
        Assert.Equal(ConversationMode.Ai, result.Mode);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(1, harness.Store.CommitCount);
        Assert.Equal(0, harness.Store.OutboundRecordCount);
    }

    [Fact]
    public async Task A_stored_handoff_reply_never_overrides_a_release_to_ai()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        harness.Store.Mode = ConversationModes.Human;
        harness.Outbox.PreStore(
            "wamid.handoff-released",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        harness.Store.OnBeginFinalOperation = () => harness.Store.Mode = ConversationModes.Ai;

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            body: "عايز أكلم حد",
            providerMessageId: "wamid.handoff-released"));

        // The conversation was a human's when this inbound was accepted, so its stored handoff effect is
        // never applied: the operator's release stands and only the lifecycle is recorded.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationMode.Ai, result.Mode);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal(0, harness.Nlu.CallCount);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.NotNull(harness.Store.LastOutboundRecordedAt);
    }

    [Fact]
    public async Task A_stored_handoff_reply_never_overrides_a_mode_the_operator_decided_after_it()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        // An earlier attempt of this inbound stored the handoff acknowledgement while the conversation was
        // still automatic, so the row records the mode decision it was authorized under.
        harness.Outbox.PreStore(
            "wamid.handoff-superseded",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        // Before the retried turn reaches its final section the operator takes the conversation over and
        // releases it back to the assistant: two explicit decisions later than the acknowledgement, and the
        // retry therefore starts automatic again.
        harness.Store.OnBeginFinalOperation = () =>
        {
            harness.Store.OperatorDecides(ConversationModes.Human);
            harness.Store.OperatorDecides(ConversationModes.Ai);
        };

        var result = await harness.ProcessAsync(ConversationSamples.Text(
            body: "عايز أكلم حد",
            providerMessageId: "wamid.handoff-superseded"));

        // The stored reply is still reconciled, but the mode effect it recorded belongs to a decision the
        // operator has already replaced, so the conversation is left exactly where the operator put it.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationMode.Ai, result.Mode);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal(2, harness.Store.ModeRevision);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
        Assert.NotNull(harness.Store.LastOutboundRecordedAt);
    }

    [Fact]
    public async Task A_stored_handoff_reply_still_completes_while_the_revision_it_names_is_current()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.Greeting)));

        // The operator took the conversation over and released it again before this acknowledgement was
        // stored, so the acknowledgement belongs to the decision that left behind, not to the first one.
        harness.Store.OperatorDecides(ConversationModes.Human);
        harness.Store.OperatorDecides(ConversationModes.Ai);

        harness.Outbox.PreStore(
            "wamid.handoff-unchanged",
            "handoff acknowledgement",
            ConversationOutboxMetadata.For(
                [],
                entersHumanMode: true,
                modeRevision: harness.Store.ModeRevision).ToJson());

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.handoff-unchanged"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(3, harness.Store.ModeRevision);
        Assert.Empty(harness.Renderer.Intents);
    }

    [Fact]
    public async Task A_handoff_that_was_already_applied_is_not_applied_again_by_a_later_retry()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        var turn = ConversationSamples.Text(providerMessageId: "wamid.handoff-idempotent");

        await harness.ProcessAsync(turn);

        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(1, harness.Store.ModeRevision);

        var rendered = harness.Renderer.Intents.Count;

        var retried = await harness.ProcessAsync(turn);

        // The acknowledgement is durable and its effect is already committed, so the retry reconciles it
        // without deciding the mode a second time.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, retried.Outcome);
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(1, harness.Store.ModeRevision);
        Assert.Single(harness.Outbox.Requests);
        Assert.Equal(rendered, harness.Renderer.Intents.Count);
    }

    [Fact]
    public async Task A_successful_handoff_records_the_mode_decision_it_was_authorized_under()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));

        // The operator already took the conversation over once and released it, so the acknowledgement is
        // authorized under a revision that is not zero.
        harness.Store.OperatorDecides(ConversationModes.Human);
        harness.Store.OperatorDecides(ConversationModes.Ai);

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.handoff-revision"));

        var stored = Assert.Single(harness.Outbox.Requests);
        var metadata = ConversationOutboxMetadata.Parse(stored.ApplicationMetadata);

        Assert.NotNull(metadata);
        Assert.True(metadata.EntersHumanMode);
        Assert.Equal(2, metadata.ModeRevision);

        // Applying the handoff is itself one mode decision.
        Assert.Equal(ConversationModes.Human, harness.Store.Mode);
        Assert.Equal(3, harness.Store.ModeRevision);
    }

    [Fact]
    public async Task An_ordinary_inbound_turn_never_advances_the_mode_revision()
    {
        var harness = StartSearchHarness();

        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.revision-1"));
        await harness.ProcessAsync(ConversationSamples.Text(providerMessageId: "wamid.revision-2"));

        // Recording inbound messages, writing the UX state and recording the outbound lifecycle are not
        // mode decisions, so they never invalidate the revision a stored handoff is checked against.
        Assert.Equal(0, harness.Store.ModeRevision);
        Assert.Equal(0, harness.Store.ModeChangeCount);
    }

    [Fact]
    public async Task A_stored_handoff_without_the_mode_revision_it_belongs_to_fails_the_turn()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.HumanHandoff)));
        harness.Outbox.PreStore("wamid.handoff-unversioned", "handoff acknowledgement", """{"v":1,"human":true}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.handoff-unversioned")));

        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Empty(harness.Renderer.Intents);
    }

    [Fact]
    public async Task A_stored_search_reply_never_overrides_a_release_to_ai()
    {
        var harness = StartSearchHarness();
        harness.Store.Mode = ConversationModes.Human;
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore(
            "wamid.search-released",
            "already shown",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());

        harness.Store.OnBeginFinalOperation = () => harness.Store.Mode = ConversationModes.Ai;

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.search-released"));

        // The list the accepted reply showed is reconciled, but the mode the operator chose is not touched.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal([10, 11], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(10, harness.Store.State.LastModelId);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_never_overrides_a_release_to_ai()
    {
        var harness = StartSearchHarness();
        harness.Store.Mode = ConversationModes.Human;
        harness.Store.State = PreviouslyShown((5, 51));
        harness.Outbox.PreStore("wamid.legacy-released", "an older reply");

        harness.Store.OnBeginFinalOperation = () => harness.Store.Mode = ConversationModes.Ai;

        var result = await harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.legacy-released"));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(KnownOutboxMessageId, result.OutboxMessageId);
        Assert.Equal(ConversationModes.Ai, harness.Store.Mode);
        Assert.Equal(0, harness.Store.ModeChangeCount);
        Assert.Equal([5], harness.Store.State.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal(5, harness.Store.State.LastModelId);
        Assert.Empty(harness.Renderer.Intents);
        Assert.Empty(harness.Outbox.Requests);
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
    public async Task A_renderer_whose_current_fact_read_fails_leaves_no_durable_reply_to_retry_against()
    {
        var harness = Harness.Start(NluAnalysisResult.Success(
            ConversationSamples.Interpretation(NluIntent.ProductSearch, brand: "Dell")));
        harness.Renderer.Fails = _ => new InvalidOperationException("The catalogue is unavailable.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(
            ConversationSamples.Text(providerMessageId: "wamid.render-failed")));

        // The failure is never answered with an invented fact: no reply was stored, so the Inbox retries
        // the same turn and the renderer reads the current facts again.
        Assert.Empty(harness.Outbox.Requests);
        Assert.Equal(0, harness.Store.OutboundRecordCount);
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
        harness.DisplayWhatTheSearchReturns();

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
        harness.DisplayWhatTheSearchReturns();

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
                ConversationId = KnownConversationId,
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

        /// <summary>
        /// Makes the fake renderer display exactly what the current search returns, which is the behaviour
        /// the deterministic renderer has: it re-runs the search and shows its current results.
        /// </summary>
        internal void DisplayWhatTheSearchReturns() =>
            Renderer.DisplayedCandidates = _ => [.. Search.Results.Select(result => (result.ModelId, result.VariantId))];

        internal Task<ConversationTurnResult> ProcessAsync(InboundTurn turn) =>
            Handler.ProcessAsync(turn);
    }
}
