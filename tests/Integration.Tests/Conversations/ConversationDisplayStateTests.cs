using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

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
        await using var host = StartSearchHost(d => doubles = d, includeRenderer: false);

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

    private ConversationsHost StartSearchHost(
        Action<ConversationDoubles> observe,
        bool includeRenderer = true,
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
            },
            overrideServices);

    private Task<string> ShortlistCountAsync(long conversationId) =>
        StateAsync(conversationId, "coalesce(jsonb_array_length(state_json->'shortlist'), 0)");

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
        DateTime? providerTimestamp = null)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                Customer,
                "text",
                providerTimestamp ?? ConversationsTestDoubles.Now,
                "عندك ديل 24؟"));
    }
}
