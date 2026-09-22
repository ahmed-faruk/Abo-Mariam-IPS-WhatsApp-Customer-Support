using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Messaging;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// Issue #11 acceptance over real PostgreSQL: one accepted inbound turn leaves at most one durable
/// outbound intent, correlated with the inbound provider message id, and no reply is ever enqueued
/// while a human holds the conversation or while the service window is closed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationOrchestrationTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Correlation = "wamid.turn-1";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_turn_stores_exactly_one_durable_outbox_row_correlated_with_the_inbound_message()
    {
        await using var host = StartHost();

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Equal(Correlation, await catalog.ScalarAsync(
            $"SELECT correlation_id FROM messaging.outbox_message WHERE id = {result.OutboxMessageId}"));
        Assert.Equal("20100000001", await catalog.ScalarAsync(
            $"SELECT customer_external_id FROM messaging.outbox_message WHERE id = {result.OutboxMessageId}"));
        Assert.Equal("AI", await catalog.ScalarAsync(
            $"SELECT sender FROM messaging.outbox_message WHERE id = {result.OutboxMessageId}"));
        Assert.Equal("the deterministic reply", await catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE id = {result.OutboxMessageId}"));
        Assert.Equal($"{result.ConversationId}", await catalog.ScalarAsync(
            $"SELECT partition_key FROM messaging.outbox_message WHERE id = {result.OutboxMessageId}"));
    }

    [Fact]
    public async Task Reprocessing_a_retried_turn_reuses_the_durable_reply_instead_of_adding_a_second_one()
    {
        await using var host = StartHost();

        var first = await ProcessAsync(host, Correlation);
        var retried = await ProcessAsync(host, Correlation);

        Assert.Equal(first.ConversationId, retried.ConversationId);
        Assert.Equal(first.OutboxMessageId, retried.OutboxMessageId);
        Assert.Equal("1", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_turn_whose_service_window_is_already_closed_leaves_no_durable_reply()
    {
        await using var host = StartHost();
        var stale = ConversationsTestDoubles.Now.AddHours(-25);

        var result = await ProcessAsync(host, Correlation, stale);

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Null(result.OutboxMessageId);
        Assert.Equal("0", await OutboxCountAsync());

        // The inbound and its lifecycle are still recorded, because only the reactive send is blocked.
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM conversations.conversation WHERE last_inbound_at IS NOT NULL"));
        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (window_expires_at = timestamptz '"
                + stale.AddHours(24).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "+00')::text FROM conversations.conversation"));
    }

    [Fact]
    public async Task A_human_conversation_leaves_no_durable_reply_for_a_later_inbound()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff)),
                renderedBody: "handoff acknowledgement"));

        var handoff = await ProcessAsync(host, Correlation);

        Assert.Equal("1", await OutboxCountAsync());

        var held = await ProcessAsync(host, "wamid.turn-2");

        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, held.Outcome);
        Assert.Null(held.OutboxMessageId);
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Single(doubles.Renderer.Intents);
        Assert.Equal(1, doubles.Nlu.CallCount);
    }

    [Fact]
    public async Task The_fail_closed_renderer_of_the_host_leaves_no_durable_reply()
    {
        await using var host = StartHost(services => services.AddConversationDoubles(
            NluAnalysisResult.Success(Interpretation(NluIntent.Greeting)),
            includeRenderer: false));

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Null(result.OutboxMessageId);
        Assert.Equal("0", await OutboxCountAsync());

        // The turn itself was still accepted and recorded.
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM conversations.conversation"));
    }

    [Fact]
    public async Task A_price_reply_reads_the_current_catalogue_price_and_never_the_stored_one()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(services => doubles = services.AddConversationDoubles(
            NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell")),
            searchResults: [Recommendation(10, 21, price: 2400)],
            publishFacts: details => details.Publish(Recommendation(10, 21, price: 2400))));

        await ProcessAsync(host, "wamid.price-1");

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, reference: "first"));

        var first = await ProcessAsync(host, "wamid.price-2");

        Assert.Equal("the deterministic reply\nprice=2400", await OutboxBodyAsync(first.OutboxMessageId));

        // The price changes in the catalogue; the stored UX state still holds only the identifiers.
        doubles.Details.Publish(Recommendation(10, 21, price: 2999));

        var second = await ProcessAsync(host, "wamid.price-3");

        Assert.Equal("the deterministic reply\nprice=2999", await OutboxBodyAsync(second.OutboxMessageId));
        Assert.DoesNotContain("2999", await catalog.ScalarAsync(
            $"SELECT state_json::text FROM conversations.conversation_state WHERE conversation_id = {second.ConversationId}"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_turn_that_reaches_the_durable_outbox_also_records_the_last_outbound()
    {
        await using var host = StartHost();

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (last_outbound_at IS NOT NULL)::text FROM conversations.conversation "
                + $"WHERE id = {result.ConversationId}"));
    }

    [Fact]
    public async Task The_inbox_worker_processor_adapter_answers_a_claimed_message_through_the_outbox()
    {
        await using var host = StartHost();

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            // The inbound carries the test clock's instant, because the service window is computed from
            // the provider timestamp and a stale one would legitimately suppress the reply.
            await inbound.EnqueueAsync(MessagingSamples.Inbound(
                Correlation,
                "20100000001",
                providerTimestamp: ConversationsTestDoubles.Now));
        }

        await using (var workerScope = host.CreateScope())
        {
            var worker = workerScope.ServiceProvider.GetRequiredService<InboxWorker>();

            Assert.Equal(1, await worker.ProcessOnceAsync());
        }

        Assert.Equal("Processed", await catalog.ScalarAsync(
            $"SELECT processing_status FROM messaging.inbox_message WHERE provider_message_id = '{Correlation}'"));
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Equal(Correlation, await catalog.ScalarAsync(
            "SELECT correlation_id FROM messaging.outbox_message"));
    }

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private Task<string> OutboxBodyAsync(long? outboxMessageId) =>
        catalog.ScalarAsync($"SELECT body FROM messaging.outbox_message WHERE id = {outboxMessageId}");

    /// <summary>A host whose deterministic seams answer a greeting.</summary>
    private ConversationsHost StartHost() =>
        StartHost(services => services.AddConversationDoubles(
            NluAnalysisResult.Success(Interpretation(NluIntent.Greeting))));

    /// <summary>A host whose deterministic seams are exactly the ones the test registers.</summary>
    private ConversationsHost StartHost(Action<IServiceCollection> configureDoubles) =>
        ConversationsHost.Start(connectionString, configureDoubles);

    private static NluInterpretation Interpretation(
        NluIntent intent,
        string? reference = null,
        string? brand = null) => new()
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
                "20100000001",
                "text",
                providerTimestamp ?? ConversationsTestDoubles.Now,
                "عندك ديل 24؟"));
    }
}
