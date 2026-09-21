using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// Issue #11 acceptance over real PostgreSQL: the customer and conversation lifecycle, the 24-hour
/// service window, and the durable AI/Human/Closed state machine of docs/TECHNICAL.md section 16.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationLifecycleTests(PostgresContainerFixture postgres) : IAsyncLifetime
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
    public async Task A_first_turn_creates_the_customer_and_one_active_ai_conversation()
    {
        await using var host = StartHost();

        var result = await ProcessAsync(host, "wamid.first", "20100000001");

        Assert.True(result.ConversationId > 0);
        Assert.Equal(ConversationMode.Ai, result.Mode);
        Assert.Equal("1", await CountAsync("conversations.customer"));
        Assert.Equal("1", await CountAsync("conversations.conversation"));
        Assert.Equal("20100000001", await catalog.ScalarAsync(
            "SELECT whatsapp_number FROM conversations.customer"));
        Assert.Equal("AI", await catalog.ScalarAsync(
            $"SELECT mode FROM conversations.conversation WHERE id = {result.ConversationId}"));
    }

    [Fact]
    public async Task A_later_turn_reuses_the_customer_and_the_active_conversation()
    {
        await using var host = StartHost();

        var first = await ProcessAsync(host, "wamid.first", "20100000001");
        var second = await ProcessAsync(host, "wamid.second", "20100000001");

        Assert.Equal(first.ConversationId, second.ConversationId);
        Assert.Equal("1", await CountAsync("conversations.customer"));
        Assert.Equal("1", await CountAsync("conversations.conversation"));
    }

    [Fact]
    public async Task The_service_window_of_the_last_inbound_is_stored_from_its_provider_timestamp()
    {
        await using var host = StartHost();
        var providerTimestamp = ConversationsTestDoubles.Now.AddHours(-2);

        var result = await ProcessAsync(host, "wamid.window", "20100000001", providerTimestamp);

        Assert.Equal("true", await IsWindowAsync(result.ConversationId, providerTimestamp.AddHours(24)));
        Assert.Equal(
            // The lifecycle records when the turn was accepted; the service window is what is computed
            // from the provider timestamp.
            ConversationsTestDoubles.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            await catalog.ScalarAsync(
                "SELECT to_char(last_inbound_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') "
                + $"FROM conversations.conversation WHERE id = {result.ConversationId}"));
    }

    [Fact]
    public async Task A_closed_conversation_is_preserved_and_the_next_inbound_creates_a_new_active_ai_one()
    {
        await using var host = StartHost();
        var first = await ProcessAsync(host, "wamid.before-close", "20100000001");

        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await control.CloseAsync(first.ConversationId));
        }

        var second = await ProcessAsync(host, "wamid.after-close", "20100000001");

        Assert.NotEqual(first.ConversationId, second.ConversationId);
        Assert.Equal(ConversationMode.Ai, second.Mode);
        Assert.Equal("2", await CountAsync("conversations.conversation"));
        Assert.Equal("1", await CountAsync("conversations.customer"));
        Assert.Equal("Closed", await catalog.ScalarAsync(
            $"SELECT mode FROM conversations.conversation WHERE id = {first.ConversationId}"));
        Assert.Equal("1", await catalog.ScalarAsync(
            $"SELECT count(*) FROM conversations.conversation WHERE id = {first.ConversationId} AND closed_at IS NOT NULL"));
        Assert.Equal("AI", await catalog.ScalarAsync(
            $"SELECT mode FROM conversations.conversation WHERE id = {second.ConversationId}"));
    }

    [Fact]
    public async Task A_closed_conversation_is_never_reopened_by_an_explicit_release()
    {
        await using var host = StartHost();
        var turn = await ProcessAsync(host, "wamid.closed", "20100000001");

        await using var scope = host.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

        Assert.Equal(ConversationModeChangeOutcome.Changed, await control.CloseAsync(turn.ConversationId));
        Assert.Equal(ConversationModeChangeOutcome.Unchanged, await control.ReleaseToAiAsync(turn.ConversationId));
        Assert.Equal("Closed", await catalog.ScalarAsync(
            $"SELECT mode FROM conversations.conversation WHERE id = {turn.ConversationId}"));
    }

    [Fact]
    public async Task A_human_handoff_is_durable_and_the_next_turn_is_recorded_without_any_ai_reply()
    {
        ConversationDoubles doubles = null!;
        var handoff = NluAnalysisResult.Success(NluInterpretationWith(NluIntent.HumanHandoff));

        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(handoff, renderedBody: "handoff"));

        var first = await ProcessAsync(host, "wamid.handoff", "20100000001");

        Assert.Equal(ConversationMode.Human, first.Mode);
        Assert.Equal(1, doubles.Nlu.CallCount);

        var second = await ProcessAsync(host, "wamid.after-handoff", "20100000001");

        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, second.Outcome);
        Assert.Equal(ConversationMode.Human, second.Mode);

        // The handoff is durable, and the second turn never reached the model, the renderer or the Outbox.
        Assert.Equal("Human", await catalog.ScalarAsync(
            $"SELECT mode FROM conversations.conversation WHERE id = {first.ConversationId}"));
        Assert.Equal("1", await CountAsync("messaging.outbox_message"));
        Assert.Equal(1, doubles.Nlu.CallCount);
        Assert.Single(doubles.Renderer.Intents);
    }

    [Fact]
    public async Task An_explicit_release_returns_a_human_conversation_to_ai()
    {
        ConversationDoubles doubles = null!;
        var handoff = NluAnalysisResult.Success(NluInterpretationWith(NluIntent.HumanHandoff));

        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(handoff, renderedBody: "handoff"));

        var first = await ProcessAsync(host, "wamid.handoff", "20100000001");

        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await control.ReleaseToAiAsync(first.ConversationId));
            Assert.Equal("AI", await catalog.ScalarAsync(
                $"SELECT mode FROM conversations.conversation WHERE id = {first.ConversationId}"));
            Assert.Equal(
                ConversationModeChangeOutcome.Unchanged,
                await control.ReleaseToAiAsync(first.ConversationId));
        }

        // A released conversation answers automatically again, so the model is consulted a second time.
        await ProcessAsync(host, "wamid.after-release", "20100000001");

        Assert.Equal(2, doubles.Nlu.CallCount);
    }

    [Fact]
    public async Task A_concurrent_first_turn_of_one_customer_converges_on_one_customer_and_one_conversation()
    {
        await using var firstHost = StartHost();
        await using var secondHost = StartHost();

        var results = await Task.WhenAll(
            ProcessAsync(firstHost, "wamid.concurrent-1", "20100000042"),
            ProcessAsync(secondHost, "wamid.concurrent-2", "20100000042"));

        Assert.Equal("1", await CountAsync("conversations.customer"));
        Assert.Equal("1", await CountAsync("conversations.conversation"));
        Assert.Equal(results[0].ConversationId, results[1].ConversationId);
    }

    private Task<string> CountAsync(string table) =>
        catalog.ScalarAsync($"SELECT count(*) FROM {table}");

    private Task<string> IsWindowAsync(long conversationId, DateTime expected) =>
        catalog.ScalarAsync(
            "SELECT (window_expires_at = timestamptz '"
            + expected.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            + "+00')::text FROM conversations.conversation "
            + $"WHERE id = {conversationId}");

    private ConversationsHost StartHost() =>
        ConversationsHost.Start(
            connectionString,
            services => services.AddConversationDoubles(
                NluAnalysisResult.Success(NluInterpretationWith(NluIntent.Greeting))));

    private static NluInterpretation NluInterpretationWith(NluIntent intent) => new()
    {
        Intent = intent,
        RequiredPorts = [],
        Grades = [],
        BudgetType = NluBudgetType.None,
    };

    private static async Task<ConversationTurnResult> ProcessAsync(
        ConversationsHost host,
        string providerMessageId,
        string customerExternalId,
        DateTime? providerTimestamp = null)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                customerExternalId,
                "text",
                providerTimestamp ?? ConversationsTestDoubles.Now,
                "عندك ديل 24؟"));
    }
}
