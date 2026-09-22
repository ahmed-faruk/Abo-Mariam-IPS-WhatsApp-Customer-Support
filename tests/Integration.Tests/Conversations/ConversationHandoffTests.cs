using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The handoff of docs/TECHNICAL.md section 16 and the approved Issue #11 decision: the conversation
/// becomes Human durably, the acknowledgement is at most one durable reply, and a turn that never got
/// its reply stored is retried instead of being skipped because the mode was already written.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationHandoffTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Customer = "20100003001";
    private const string Correlation = "wamid.handoff-1";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_handoff_stores_exactly_one_acknowledgement_and_enters_human()
    {
        await using var host = StartHost();

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Equal(Correlation, await catalog.ScalarAsync(
            "SELECT correlation_id FROM messaging.outbox_message"));
    }

    [Fact]
    public async Task A_handoff_whose_first_outbox_attempt_failed_enters_human_only_when_a_later_attempt_is_stored()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff)),
                renderedBody: "handoff acknowledgement"),
            services => services.AddFirstAttemptFailingOutbox());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessAsync(host, Correlation));

        // The failed attempt must not leave a durable Human mode behind: that is exactly what would make
        // the retry exit as AwaitingHuman and lose the customer's handoff acknowledgement forever.
        Assert.Equal("AI", await catalog.ScalarAsync(
            "SELECT mode FROM conversations.conversation ORDER BY id LIMIT 1"));
        Assert.Equal("0", await OutboxCountAsync());

        var retried = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, retried.Outcome);
        Assert.Equal("Human", await ModeAsync(retried.ConversationId));
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Equal(Correlation, await catalog.ScalarAsync(
            "SELECT correlation_id FROM messaging.outbox_message"));
    }

    [Fact]
    public async Task A_handoff_retried_after_its_acknowledgement_was_already_stored_reuses_that_row_and_enters_human()
    {
        ConversationDoubles doubles = null!;
        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.Greeting)),
                renderedBody: "the deterministic reply"));

        // The conversation exists as an ordinary automatic one, because the handoff attempt is the turn
        // that is being retried.
        var created = await ProcessAsync(host, "wamid.handoff-before");

        // A previous attempt stored the acknowledgement and then died before the Conversations commit, so
        // the retry finds the reply of its own correlation already durable.
        long storedId;

        await using (var scope = host.CreateScope())
        {
            storedId = (await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
                new OutboundMessageRequest(
                    created.ConversationId,
                    Customer,
                    Correlation,
                    "AI",
                    "handoff acknowledgement",
                    ConversationOutboxMetadata.For([], entersHumanMode: true).ToJson()))).OutboxMessageId;
        }

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff));

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));

        // The greeting reply of the first turn plus the reused acknowledgement: still one reply per turn.
        Assert.Equal("2", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_handoff_retried_after_the_window_closed_still_reconciles_its_stored_acknowledgement()
    {
        ConversationDoubles doubles = null!;
        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.Greeting)),
                renderedBody: "the deterministic reply"));

        var created = await ProcessAsync(host, "wamid.handoff-window-before");
        var renderCalls = doubles.Renderer.Intents.Count;

        // An earlier attempt of the retried turn stored the acknowledgement and then died before its
        // Conversations commit.
        long storedId;

        await using (var scope = host.CreateScope())
        {
            storedId = (await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
                new OutboundMessageRequest(
                    created.ConversationId,
                    Customer,
                    Correlation,
                    "AI",
                    "handoff acknowledgement",
                    ConversationOutboxMetadata.For([], entersHumanMode: true).ToJson()))).OutboxMessageId;
        }

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff));

        // The retry arrives after the 24-hour window has closed. The acknowledgement is not a new send, so
        // the closed window may not stop the retry from completing the handoff it already made durable.
        var result = await ProcessAsync(host, Correlation, ConversationsTestDoubles.Now.AddHours(-25));

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("2", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_stored_reply_without_metadata_does_not_become_a_handoff_just_because_the_retry_repeats_one()
    {
        ConversationDoubles doubles = null!;
        await using var host = ConversationsHost.Start(
            connectionString,
            services => doubles = services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.Greeting)),
                renderedBody: "the deterministic reply"));

        var created = await ProcessAsync(host, "wamid.handoff-legacy-before");
        var renderCalls = doubles.Renderer.Intents.Count;

        // A reply stored before metadata existed owns the correlation but carries no evidence of what its
        // own body meant.
        long storedId;

        await using (var scope = host.CreateScope())
        {
            storedId = (await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
                new OutboundMessageRequest(
                    created.ConversationId,
                    Customer,
                    Correlation,
                    "AI",
                    "an older reply"))).OutboxMessageId;
        }

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff));

        var result = await ProcessAsync(host, Correlation);

        // The row is reused, but the freshly computed route is not proof of what it contained, so the
        // conversation stays automatic.
        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, result.Outcome);
        Assert.Equal(storedId, result.OutboxMessageId);
        Assert.Equal("AI", await ModeAsync(result.ConversationId));
        Assert.Equal(renderCalls, doubles.Renderer.Intents.Count);
        Assert.Equal("2", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_handoff_of_the_fail_closed_renderer_enters_human_without_a_reply()
    {
        await using var host = ConversationsHost.Start(
            connectionString,
            services =>
            {
                services.AddConversationDoubles(
                    NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff)),
                    includeRenderer: false);
                services.AddFailClosedRenderer();
            });

        var result = await ProcessAsync(host, Correlation);

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));
        Assert.Equal("0", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_handoff_whose_service_window_is_closed_enters_human_without_a_reply()
    {
        await using var host = StartHost();

        var result = await ProcessAsync(
            host,
            Correlation,
            ConversationsTestDoubles.Now.AddHours(-25));

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal("Human", await ModeAsync(result.ConversationId));
        Assert.Equal("0", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_later_inbound_of_a_human_conversation_never_reaches_the_model_the_renderer_or_the_outbox()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(services => doubles = services.AddConversationDoubles(
            NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff)),
            renderedBody: "handoff acknowledgement"));

        var handoff = await ProcessAsync(host, Correlation);
        var callsAfterHandoff = doubles.Nlu.CallCount;
        var rendererCallsAfterHandoff = doubles.Renderer.Intents.Count;

        doubles.Nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.Greeting));

        var held = await ProcessAsync(host, "wamid.handoff-later");

        Assert.Equal(ConversationTurnOutcome.AwaitingHuman, held.Outcome);
        Assert.Equal(handoff.ConversationId, held.ConversationId);
        Assert.Equal(callsAfterHandoff, doubles.Nlu.CallCount);
        Assert.Equal(rendererCallsAfterHandoff, doubles.Renderer.Intents.Count);
        Assert.Equal("1", await OutboxCountAsync());
    }

    /// <summary>A host whose deterministic seams answer the customer's handoff request.</summary>
    private ConversationsHost StartHost() =>
        StartHost(
            services => services.AddConversationDoubles(
                NluAnalysisResult.Success(Interpretation(NluIntent.HumanHandoff)),
                renderedBody: "handoff acknowledgement"));

    private ConversationsHost StartHost(Action<IServiceCollection> configureDoubles) =>
        ConversationsHost.Start(connectionString, configureDoubles);

    private ConversationsHost StartHost(
        Action<IServiceCollection> configureDoubles,
        Action<IServiceCollection> overrideServices) =>
        ConversationsHost.Start(connectionString, configureDoubles, overrideServices);

    private Task<string> ModeAsync(long conversationId) =>
        catalog.ScalarAsync($"SELECT mode FROM conversations.conversation WHERE id = {conversationId}");

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private static NluInterpretation Interpretation(NluIntent intent) => new()
    {
        Intent = intent,
        RequiredPorts = [],
        Grades = [],
        BudgetType = NluBudgetType.None,
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
                "عايز أكلم حد"));
    }
}
