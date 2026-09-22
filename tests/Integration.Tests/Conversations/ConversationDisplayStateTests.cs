using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The list a reply shows is the list the conversation may reference. Over real PostgreSQL and the real
/// durable Outbox, a search only becomes addressable once its reply was stored, and every path that
/// cannot deliver the reply - a closed service window, the fail-closed renderer, a failed enqueue -
/// leaves the conversation referencing only what the customer really saw.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationDisplayStateTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Customer = "20100004001";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_stored_search_reply_makes_its_first_result_addressable()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var search = await ProcessAsync(host, "wamid.display-1");

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, search.Outcome);
        Assert.Equal("2", await ShortlistCountAsync(search.ConversationId));
        Assert.Equal("10", await StateAsync(search.ConversationId, "state_json->>'lastModelId'"));

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, "first"));

        var price = await ProcessAsync(host, "wamid.display-2");

        // The customer's "first" resolved to the candidate the stored list really showed.
        Assert.Equal("the deterministic reply\nprice=2400", await OutboxBodyAsync(price.OutboxMessageId));
    }

    [Fact]
    public async Task A_search_whose_service_window_was_closed_does_not_make_a_shortlist_addressable()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var search = await ProcessAsync(
            host,
            "wamid.display-closed",
            ConversationsTestDoubles.Now.AddHours(-25));

        Assert.Equal(ConversationTurnOutcome.NoResponse, search.Outcome);
        Assert.Equal("0", await ShortlistCountAsync(search.ConversationId));
        Assert.Equal("0", await OutboxCountAsync());

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, "first"));

        await ProcessAsync(host, "wamid.display-closed-2");

        Assert.Equal(ConversationResponseKind.Clarification, doubles.Renderer.Intents[^1].Kind);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, doubles.Renderer.Intents[^1].ReasonCode);
    }

    [Fact]
    public async Task A_search_of_the_fail_closed_renderer_does_not_make_a_shortlist_addressable()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d, includeRenderer: false, failClosedRenderer: true);

        var search = await ProcessAsync(host, "wamid.display-unbound");

        Assert.Equal(ConversationTurnOutcome.NoResponse, search.Outcome);
        Assert.Equal("0", await ShortlistCountAsync(search.ConversationId));
        Assert.Equal("0", await OutboxCountAsync());

        // The filters the customer stated are still context for the next turn, which is answered by the
        // renderer the test binds for the follow-up.
        Assert.Equal("Dell", await StateAsync(search.ConversationId, "state_json#>>'{lastFilters,brand}'"));
    }

    [Fact]
    public async Task A_search_whose_outbox_failed_does_not_make_a_shortlist_addressable_until_the_retry_is_stored()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(
            d => doubles = d,
            overrideServices: services => services.AddFirstAttemptFailingOutbox());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessAsync(host, "wamid.display-retry"));

        var conversationId = long.Parse(await ConversationIdAsync(), CultureInfo.InvariantCulture);

        // The reply never became durable, so nothing of it may be resolvable yet.
        Assert.Equal("0", await ShortlistCountAsync(conversationId));
        Assert.Equal("0", await OutboxCountAsync());

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, "first"));
        await ProcessAsync(host, "wamid.display-retry-probe");

        Assert.Equal(ConversationResponseKind.Clarification, doubles.Renderer.Intents[^1].Kind);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, doubles.Renderer.Intents[^1].ReasonCode);

        // The Inbox retries the same turn, its reply is now stored, and only then does the list it showed
        // become the list the conversation can reference.
        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"));
        var retried = await ProcessAsync(host, "wamid.display-retry");

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, retried.Outcome);
        Assert.Equal("2", await ShortlistCountAsync(conversationId));
        Assert.Equal("10", await StateAsync(conversationId, "state_json->>'lastModelId'"));
    }

    [Fact]
    public async Task A_search_retry_after_the_window_closed_still_reconciles_the_reply_it_already_stored()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var created = await ProcessAsync(host, "wamid.closed-retry-first");
        var storedId = await StoreAcceptedReplyAsync(
            host,
            created.ConversationId,
            "wamid.closed-retry",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
                entersHumanMode: false).ToJson());
        var renderCalls = doubles.Renderer.Intents.Count;

        // The retry arrives after the 24-hour window has closed.
        var result = await ProcessAsync(
            host,
            "wamid.closed-retry",
            ConversationsTestDoubles.Now.AddHours(-25));

        // A reply that is already durable is not a new free-form send, so the closed window may not make
        // the retry forget it: the stored row is reconciled and nothing is rendered or stored again.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("2", await OutboxCountAsync());
        Assert.Equal("2", await ShortlistCountAsync(result.ConversationId));
        Assert.Equal("10", await StateAsync(result.ConversationId, "state_json->>'lastModelId'"));
        Assert.Equal("21", await StateAsync(result.ConversationId, "state_json->>'lastVariantId'"));
    }

    [Fact]
    public async Task A_reply_this_turn_already_stored_is_reconciled_without_reopening_a_closed_conversation()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var created = await ProcessAsync(host, "wamid.closed-mode-first");
        var storedId = await StoreAcceptedReplyAsync(
            host,
            created.ConversationId,
            "wamid.closed-mode",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: true,
                modeRevision: await RevisionAsync(created.ConversationId)).ToJson());
        var renderCalls = doubles.Renderer.Intents.Count;

        // The operator closes the conversation while this turn is still interpreting the message, after an
        // earlier attempt had already stored the acknowledgement.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff));
        doubles.Nlu.Gate = gate;

        var turn = ProcessAsync(host, "wamid.closed-mode");
        await doubles.Nlu.Entered;

        await using (var scope = host.CreateScope())
        {
            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await scope.ServiceProvider.GetRequiredService<IConversationModeControl>()
                    .CloseAsync(created.ConversationId));
        }

        gate.SetResult();
        var result = await turn;

        // The already accepted reply is reconciled, but its handoff effect never reopens the closed
        // conversation, and no new reply is rendered or stored.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal("Closed", await ModeAsync(result.ConversationId));
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("2", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_search_retry_that_starts_in_human_mode_still_reconciles_the_reply_it_already_stored()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        // The first turn of this conversation displayed two products.
        var created = await ProcessAsync(host, "wamid.human-retry-first");

        Assert.Equal("2", await ShortlistCountAsync(created.ConversationId));

        // An earlier attempt of the retried turn stored a reply that displayed one product, and then died
        // before its Conversations change was committed.
        var storedId = await StoreAcceptedReplyAsync(
            host,
            created.ConversationId,
            "wamid.human-retry",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: false).ToJson());
        var renderCalls = doubles.Renderer.Intents.Count;

        // The operator takes the conversation over before the retry runs, so the retry is accepted by a
        // conversation that no longer answers automatically.
        await using (var scope = host.CreateScope())
        {
            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await scope.ServiceProvider.GetRequiredService<IConversationModeControl>()
                    .TakeOverAsync(created.ConversationId));
        }

        var result = await ProcessAsync(host, "wamid.human-retry");

        // The already durable reply is still reconciled, but only from its stored metadata: no
        // interpretation, no renderer, no second reply, and Human is never released.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("2", await OutboxCountAsync());
        Assert.Equal("1", await ShortlistCountAsync(result.ConversationId));
        Assert.Equal("10", await StateAsync(result.ConversationId, "state_json->>'lastModelId'"));
        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (last_outbound_at IS NOT NULL)::text FROM conversations.conversation "
                + $"WHERE id = {result.ConversationId}"));
    }

    [Fact]
    public async Task A_retry_whose_reply_was_accepted_by_a_closed_conversation_never_lands_on_the_new_one()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        // The first turn belongs to conversation A and displayed two products.
        var closed = await ProcessAsync(host, "wamid.other-conversation-first");

        Assert.Equal("2", await ShortlistCountAsync(closed.ConversationId));

        // An earlier attempt of the retried inbound stored its reply for A - a handoff acknowledgement that
        // also claims a displayed list - and then died before its Conversations change was committed.
        var storedId = await StoreAcceptedReplyAsync(
            host,
            closed.ConversationId,
            "wamid.other-conversation-retry",
            ConversationOutboxMetadata.For(
                [new ConversationDisplayedCandidate(10, 21)],
                entersHumanMode: true,
                modeRevision: await RevisionAsync(closed.ConversationId)).ToJson());

        // The operator closes A before the Inbox retries the same provider message.
        await using (var scope = host.CreateScope())
        {
            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await scope.ServiceProvider.GetRequiredService<IConversationModeControl>()
                    .CloseAsync(closed.ConversationId));
        }

        var renderCalls = doubles.Renderer.Intents.Count;
        var nluCalls = doubles.Nlu.CallCount;
        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff));

        // The retried inbound names the closed conversation, so OpenTurn resolves the new active one.
        var retried = await ProcessAsync(
            host,
            "wamid.other-conversation-retry",
            conversationId: closed.ConversationId);

        // The durable reply belongs to A, so nothing of it may be reconciled onto the new conversation:
        // not its list, not its handoff effect, not its outbound lifecycle, and not the retried inbound
        // either. The turn is a safe non-send for the conversation that never answered this message.
        Assert.NotEqual(closed.ConversationId, retried.ConversationId);
        Assert.Equal(ConversationTurnOutcome.NoResponse, retried.Outcome);
        Assert.Null(retried.OutboxMessageId);
        Assert.Equal(ConversationMode.Ai, retried.Mode);
        Assert.Equal("0", await StateCountAsync(retried.ConversationId));
        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (last_outbound_at IS NULL)::text FROM conversations.conversation "
                + $"WHERE id = {retried.ConversationId}"));
        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (last_inbound_at IS NULL AND window_expires_at IS NULL)::text "
                + $"FROM conversations.conversation WHERE id = {retried.ConversationId}"));

        // Nothing is interpreted, rendered or stored a second time, A stays closed, and the durable reply
        // is still exactly the one row the closed conversation accepted.
        Assert.Equal(nluCalls, doubles.Nlu.CallCount);
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("Closed", await ModeAsync(closed.ConversationId));
        Assert.Equal("2", await OutboxCountAsync());
        Assert.Equal(
            closed.ConversationId.ToString(CultureInfo.InvariantCulture),
            await catalog.ScalarAsync(
                $"SELECT conversation_id FROM messaging.outbox_message WHERE id = {storedId}"));
    }

    private static async Task<long> StoreAcceptedReplyAsync(
        ConversationsHost host,
        long conversationId,
        string correlationId,
        string applicationMetadata)
    {
        await using var scope = host.CreateScope();

        var accepted = await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
            new OutboundMessageRequest(
                conversationId,
                Customer,
                correlationId,
                "AI",
                "already shown",
                applicationMetadata));

        return accepted.OutboxMessageId;
    }

    private Task<string> ModeAsync(long conversationId) =>
        catalog.ScalarAsync($"SELECT mode FROM conversations.conversation WHERE id = {conversationId}");

    /// <summary>How many explicit mode decisions the stored conversation has had.</summary>
    private async Task<long> RevisionAsync(long conversationId) =>
        long.Parse(
            await catalog.ScalarAsync(
                $"SELECT mode_revision FROM conversations.conversation WHERE id = {conversationId}"),
            CultureInfo.InvariantCulture);

    private ConversationsHost StartSearchHost(
        Action<ConversationDoubles> observe,
        bool includeRenderer = true,
        bool failClosedRenderer = false,
        Action<IServiceCollection>? overrideServices = null) =>
        ConversationsHost.Start(
            connectionString,
            services =>
            {
                var doubles = services.AddConversationDoubles(
                    NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell")),
                    renderedBody: "the deterministic reply",
                    searchResults:
                    [
                        Recommendation(10, 21, price: 2400),
                        Recommendation(11, 25, price: 2600),
                    ],
                    publishFacts: details =>
                    {
                        details.Publish(Recommendation(10, 21, price: 2400));
                        details.Publish(Recommendation(11, 25, price: 2600));
                    },
                    includeRenderer: includeRenderer);

                observe(doubles);

                if (failClosedRenderer)
                {
                    services.AddFailClosedRenderer();
                }
            },
            overrideServices);

    private Task<string> ShortlistCountAsync(long conversationId) =>
        StateAsync(conversationId, "coalesce(jsonb_array_length(state_json->'shortlist'), 0)");

    private Task<string> StateCountAsync(long conversationId) =>
        catalog.ScalarAsync(
            $"SELECT count(*) FROM conversations.conversation_state WHERE conversation_id = {conversationId}");

    private Task<string> StateAsync(long conversationId, string expression) =>
        catalog.ScalarAsync(
            $"SELECT {expression} FROM conversations.conversation_state "
            + $"WHERE conversation_id = {conversationId}");

    private Task<string> ConversationIdAsync() =>
        catalog.ScalarAsync("SELECT id FROM conversations.conversation ORDER BY id LIMIT 1");

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private Task<string> OutboxBodyAsync(long? outboxMessageId) =>
        catalog.ScalarAsync($"SELECT body FROM messaging.outbox_message WHERE id = {outboxMessageId}");

    private static NluInterpretation Interpretation(NluIntent intent, string? reference = null, string? brand = null) =>
        new()
        {
            Intent = intent,
            Reference = reference,
            Brand = brand,
            RequiredPorts = [],
            Grades = [],
            BudgetType = NluBudgetType.None,
        };

    private static ProductRecommendation Recommendation(long modelId, long variantId, decimal price) => new()
    {
        ModelId = modelId,
        VariantId = variantId,
        ModelCode = $"M{modelId}",
        Brand = "Dell",
        DisplayName = $"Dell {modelId}",
        Price = price,
        Quantity = 3,
        IsAvailable = true,
    };

    private static async Task<ConversationTurnResult> ProcessAsync(
        ConversationsHost host,
        string providerMessageId,
        DateTime? providerTimestamp = null,
        long? conversationId = null)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                Customer,
                "text",
                providerTimestamp ?? ConversationsTestDoubles.Now,
                "عندك ديل 24؟",
                conversationId));
    }
}
