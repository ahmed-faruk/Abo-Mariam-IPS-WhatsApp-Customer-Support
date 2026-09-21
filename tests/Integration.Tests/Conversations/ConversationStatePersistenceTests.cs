using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The UX state of docs/TECHNICAL.md section 13 as it is really stored: one JSONB document, one sliding
/// 30-minute expiry, treated as empty when it is expired or malformed, and never the authority for a
/// commercial fact.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationStatePersistenceTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_successful_search_stores_identifiers_and_filters_as_jsonb()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var result = await ProcessAsync(host, "wamid.state-1");

        // PostgreSQL reformats jsonb, so the assertions read the document through its own operators
        // instead of comparing a serialization format.
        Assert.Equal("2", await StatePathAsync(result.ConversationId, "jsonb_array_length(state_json->'shortlist')"));
        Assert.Equal("10", await StatePathAsync(result.ConversationId, "state_json->>'lastModelId'"));
        Assert.Equal("21", await StatePathAsync(result.ConversationId, "state_json->>'lastVariantId'"));
        Assert.Equal("ProductSearch", await StatePathAsync(result.ConversationId, "state_json->>'lastIntent'"));
        Assert.Equal("Dell", await StatePathAsync(result.ConversationId, "state_json#>>'{lastFilters,brand}'"));
        Assert.Equal("10", await StatePathAsync(result.ConversationId, "state_json#>>'{shortlist,0,modelId}'"));
        Assert.Equal("1", await StatePathAsync(result.ConversationId, "state_json#>>'{shortlist,0,position}'"));
        Assert.Equal("25", await StatePathAsync(result.ConversationId, "state_json#>>'{shortlist,1,variantId}'"));
        Assert.Single(doubles.Renderer.Intents);
    }

    [Fact]
    public async Task Every_successful_state_write_sets_the_expiry_thirty_minutes_ahead()
    {
        await using var host = StartSearchHost();

        var result = await ProcessAsync(host, "wamid.state-2");

        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (expires_at = timestamptz '"
                + ConversationsTestDoubles.Now.AddMinutes(30)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "+00')::text FROM conversations.conversation_state "
                + $"WHERE conversation_id = {result.ConversationId}"));
    }

    [Fact]
    public async Task The_stored_state_never_holds_a_commercial_fact()
    {
        await using var host = StartSearchHost();

        var result = await ProcessAsync(host, "wamid.state-3");
        var json = await StateJsonAsync(result.ConversationId);

        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warranty", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("available", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2400", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_state_is_read_as_empty_so_a_positional_reference_cannot_resolve()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var first = await ProcessAsync(host, "wamid.expired-1");

        await ExpireStateAsync(first.ConversationId);

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, reference: "first"));

        await ProcessAsync(host, "wamid.expired-2");

        var intent = doubles.Renderer.Intents[^1];

        Assert.Equal(ConversationResponseKind.Clarification, intent.Kind);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, intent.ReasonCode);
    }

    [Fact]
    public async Task An_expired_state_is_replaced_by_the_next_successful_write()
    {
        await using var host = StartSearchHost();

        var first = await ProcessAsync(host, "wamid.replace-1");

        await ExpireStateAsync(first.ConversationId);

        await ProcessAsync(host, "wamid.replace-2");

        Assert.Equal(
            "true",
            await catalog.ScalarAsync(
                "SELECT (expires_at = timestamptz '"
                + ConversationsTestDoubles.Now.AddMinutes(30)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "+00')::text FROM conversations.conversation_state "
                + $"WHERE conversation_id = {first.ConversationId}"));
        Assert.Equal(
            "1",
            await catalog.ScalarAsync(
                "SELECT count(*) FROM conversations.conversation_state "
                + $"WHERE conversation_id = {first.ConversationId}"));
        Assert.Contains("\"shortlist\"", await StateJsonAsync(first.ConversationId), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"shortlist":"not an array"}""")]
    [InlineData("""{"shortlist":null}""")]
    public async Task A_malformed_state_is_read_as_empty_instead_of_breaking_the_turn(string storedJson)
    {
        ConversationDoubles doubles = null!;
        await using var host = StartSearchHost(d => doubles = d);

        var first = await ProcessAsync(host, "wamid.malformed-1");

        await catalog.ExecuteAsync(
            $"UPDATE conversations.conversation_state SET state_json = '{storedJson}'::jsonb "
            + $"WHERE conversation_id = {first.ConversationId}");

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, reference: "first"));

        var result = await ProcessAsync(host, "wamid.malformed-2");

        Assert.Equal(ConversationResponseKind.Clarification, doubles.Renderer.Intents[^1].Kind);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, doubles.Renderer.Intents[^1].ReasonCode);
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
    }

    private ConversationsHost StartSearchHost(Action<ConversationDoubles>? observe = null)
    {
        var analysis = NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"));

        return ConversationsHost.Start(
            connectionString,
            services =>
            {
                var doubles = services.AddConversationDoubles(
                    analysis,
                    searchResults:
                    [
                        Recommendation(10, 21, price: 2400),
                        Recommendation(11, 25, price: 2600),
                    ]);

                observe?.Invoke(doubles);
            });
    }

    private Task<string> StateJsonAsync(long conversationId) =>
        catalog.ScalarAsync(
            $"SELECT state_json::text FROM conversations.conversation_state WHERE conversation_id = {conversationId}");

    private Task<string> StatePathAsync(long conversationId, string expression) =>
        catalog.ScalarAsync(
            $"SELECT {expression} FROM conversations.conversation_state WHERE conversation_id = {conversationId}");

    /// <summary>Expires the stored state at the test clock's own instant, not at the database clock's.</summary>
    private Task ExpireStateAsync(long conversationId) =>
        catalog.ExecuteAsync(
            "UPDATE conversations.conversation_state SET expires_at = timestamptz '"
            + ConversationsTestDoubles.Now.AddMinutes(-1)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            + "+00' WHERE conversation_id = " + conversationId.ToString(CultureInfo.InvariantCulture));

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
        string providerMessageId)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                "20100000001",
                "text",
                ConversationsTestDoubles.Now,
                "the customer message"));
    }
}
