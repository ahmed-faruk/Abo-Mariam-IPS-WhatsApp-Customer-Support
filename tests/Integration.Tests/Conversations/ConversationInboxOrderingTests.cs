using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Messaging;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The ordering of docs/TECHNICAL.md section 15 and section 25.1, proven through the real Messaging Inbox
/// path instead of a second coordination mechanism inside Conversations: two inbound messages of one
/// customer go into the durable Inbox, the real worker drains them one at a time, and the conversation's
/// UX state ends up as the ordered merge of both turns with neither list nor filter lost.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationInboxOrderingTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Customer = "20100005001";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Two_inbound_messages_of_one_customer_are_processed_in_order_and_merge_their_state()
    {
        ConversationDoubles doubles = null!;
        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell", sizeInches: 24)),
                renderedBody: "the deterministic reply",
                searchResults:
                [
                    Recommendation(10, 21),
                    Recommendation(11, 25),
                ]));

        await EnqueueAsync(host, "wamid.order-1", "عندك ديل 24؟");
        await EnqueueAsync(host, "wamid.order-2", "عايز IPS وفي HDMI");

        // The first poll claims and processes only the oldest message of the customer's partition.
        Assert.Equal(1, await PollAsync(host));

        var conversationId = long.Parse(
            await catalog.ScalarAsync("SELECT id FROM conversations.conversation ORDER BY id LIMIT 1"),
            CultureInfo.InvariantCulture);

        Assert.Equal("Processed", await InboxStatusAsync("wamid.order-1"));
        Assert.Equal("Pending", await InboxStatusAsync("wamid.order-2"));
        Assert.Equal("0", await InboxAttemptsAsync("wamid.order-2"));

        // The first turn's filters and the list it showed are stored.
        Assert.Equal("Dell", await StateAsync(conversationId, "state_json#>>'{lastFilters,brand}'"));
        Assert.Equal("24", await StateAsync(conversationId, "state_json#>>'{lastFilters,sizeInches}'"));
        Assert.Equal("2", await ShortlistCountAsync(conversationId));
        Assert.Equal("1", await OutboxCountAsync());

        // The second turn is the refinement of the first, so it can only run after it.
        doubles.Nlu.Analysis = NluAnalysisResult.Success(
            Interpretation(NluIntent.ProductSearch, panel: "IPS", requiredPorts: ["HDMI"]));
        doubles.Search.Results = [Recommendation(12, 31), Recommendation(13, 35)];

        Assert.Equal(1, await PollAsync(host));

        Assert.Equal("Processed", await InboxStatusAsync("wamid.order-2"));

        // The ordered merge keeps every filter the customer stated and the list of the newer search.
        Assert.Equal("Dell", await StateAsync(conversationId, "state_json#>>'{lastFilters,brand}'"));
        Assert.Equal("24", await StateAsync(conversationId, "state_json#>>'{lastFilters,sizeInches}'"));
        Assert.Equal("IPS", await StateAsync(conversationId, "state_json#>>'{lastFilters,panel}'"));
        Assert.Equal("HDMI", await StateAsync(conversationId, "state_json#>>'{lastFilters,requiredPorts,0}'"));
        Assert.Equal("2", await ShortlistCountAsync(conversationId));
        Assert.Equal("12", await StateAsync(conversationId, "state_json->>'lastModelId'"));

        // One durable reply per accepted turn, each correlated with its own inbound message.
        Assert.Equal("2", await OutboxCountAsync());
        Assert.Equal("2", await catalog.ScalarAsync(
            "SELECT count(DISTINCT correlation_id) FROM messaging.outbox_message"));

        // Both turns of the same customer share the one conversation the ordered path created.
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM conversations.conversation"));
    }

    private static async Task EnqueueAsync(ConversationsHost host, string providerMessageId, string body)
    {
        await using var scope = host.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>().EnqueueAsync(
            MessagingSamples.Inbound(
                providerMessageId,
                Customer,
                body,
                providerTimestamp: ConversationsTestDoubles.Now));
    }

    /// <summary>One real Inbox worker poll, which is what the hosted service loops over.</summary>
    private static async Task<int> PollAsync(ConversationsHost host)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<InboxWorker>().ProcessOnceAsync();
    }

    private Task<string> InboxStatusAsync(string providerMessageId) =>
        catalog.ScalarAsync(
            "SELECT processing_status FROM messaging.inbox_message "
            + $"WHERE provider_message_id = '{providerMessageId}'");

    private Task<string> InboxAttemptsAsync(string providerMessageId) =>
        catalog.ScalarAsync(
            "SELECT attempts FROM messaging.inbox_message "
            + $"WHERE provider_message_id = '{providerMessageId}'");

    private Task<string> StateAsync(long conversationId, string expression) =>
        catalog.ScalarAsync(
            $"SELECT {expression} FROM conversations.conversation_state "
            + $"WHERE conversation_id = {conversationId}");

    private Task<string> ShortlistCountAsync(long conversationId) =>
        StateAsync(conversationId, "coalesce(jsonb_array_length(state_json->'shortlist'), 0)");

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private static NluInterpretation Interpretation(
        NluIntent intent,
        string? brand = null,
        decimal? sizeInches = null,
        string? panel = null,
        IReadOnlyList<string>? requiredPorts = null) => new()
        {
            Intent = intent,
            Brand = brand,
            SizeInches = sizeInches,
            Panel = panel,
            RequiredPorts = requiredPorts ?? [],
            Grades = [],
            BudgetType = NluBudgetType.None,
        };

    private static ProductRecommendation Recommendation(long modelId, long variantId) => new()
    {
        ModelId = modelId,
        VariantId = variantId,
        ModelCode = $"M{modelId}",
        Brand = "Dell",
        DisplayName = $"Dell {modelId}",
        Price = 2400,
        Quantity = 3,
        IsAvailable = true,
    };
}
