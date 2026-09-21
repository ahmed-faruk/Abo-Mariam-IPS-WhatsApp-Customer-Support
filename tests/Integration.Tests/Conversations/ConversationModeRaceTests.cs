using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The final authorization of an automatic turn and the operator's explicit mode changes are ordered by
/// the same Conversations-owned PostgreSQL lock. These tests drive both sides with real persistence and
/// two service scopes, so whichever operation wins is visible in the database and in the durable Outbox.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationModeRaceTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Customer = "20100002001";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_takeover_that_wins_the_final_authorization_suppresses_the_pending_reply()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        doubles.Nlu.Analysis = NluAnalysisResult.Success(NluInterpretationWith(NluIntent.HumanHandoff));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doubles.Nlu.Gate = gate;

        var turn = ProcessAsync(host, "wamid.race-takeover");
        await doubles.Nlu.Entered;

        // The operator wins the conversation while the turn is still interpreting the message.
        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await control.TakeOverAsync(conversationId));
        }

        Assert.Equal("Human", await ModeAsync(conversationId));

        gate.SetResult();
        var result = await turn;

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Human, result.Mode);
        Assert.Equal("Human", await ModeAsync(conversationId));

        // No reply may be enqueued for a conversation the operator owns.
        Assert.Equal("1", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_close_that_wins_the_final_authorization_suppresses_the_pending_reply()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        doubles.Nlu.Gate = gate;

        var turn = ProcessAsync(host, "wamid.race-close");
        await doubles.Nlu.Entered;

        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            Assert.Equal(ConversationModeChangeOutcome.Changed, await control.CloseAsync(conversationId));
        }

        gate.SetResult();
        var result = await turn;

        Assert.Equal(ConversationTurnOutcome.NoResponse, result.Outcome);
        Assert.Equal(ConversationMode.Closed, result.Mode);
        Assert.Equal("Closed", await ModeAsync(conversationId));
        Assert.Equal("1", await OutboxCountAsync());
    }

    [Fact]
    public async Task An_automatic_turn_that_enqueued_first_still_allows_a_takeover_afterwards()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        // The turn is not blocked anywhere, so it wins the final authorization and its reply is durable.
        var turn = await ProcessAsync(host, "wamid.race-reply-first");

        Assert.Equal(ConversationTurnOutcome.ResponseEnqueued, turn.Outcome);
        Assert.Equal("2", await OutboxCountAsync());
        Assert.Equal("AI", await ModeAsync(conversationId));

        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            // The operator's takeover still applies, it simply applies after the durable reply.
            Assert.Equal(ConversationModeChangeOutcome.Changed, await control.TakeOverAsync(conversationId));
        }

        Assert.Equal("Human", await ModeAsync(conversationId));
        Assert.Equal("2", await OutboxCountAsync());
    }

    [Fact]
    public async Task A_mode_change_of_one_scope_waits_for_the_final_operation_of_another()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        await using var holder = host.CreateScope();
        var coordinator = holder.ServiceProvider.GetRequiredService<ConversationOperationCoordinator>();
        await using var operation = await coordinator.BeginAsync(conversationId, CancellationToken.None);

        await using var contender = host.CreateScope();
        var control = contender.ServiceProvider.GetRequiredService<IConversationModeControl>();
        var takeover = control.TakeOverAsync(conversationId);

        // The lock is held by the first operation, so the operator action cannot have applied yet.
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.False(takeover.IsCompleted);
        Assert.Equal("AI", await ModeAsync(conversationId));

        await operation.CommitAsync(CancellationToken.None);

        Assert.Equal(ConversationModeChangeOutcome.Changed, await takeover.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal("Human", await ModeAsync(conversationId));
    }

    [Fact]
    public async Task The_conversation_lock_is_released_when_an_operation_throws()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        await using (var scope = host.CreateScope())
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<ConversationOperationCoordinator>();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var operation = await coordinator.BeginAsync(conversationId, CancellationToken.None);

                throw new InvalidOperationException("The operation failed after it took the lock.");
            });
        }

        // The failed operation released the lock with its transaction, so another scope can take the
        // conversation immediately instead of waiting for a connection to be recycled.
        await using (var scope = host.CreateScope())
        {
            var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

            Assert.Equal(
                ConversationModeChangeOutcome.Changed,
                await control.TakeOverAsync(conversationId).WaitAsync(TimeSpan.FromSeconds(15)));
        }

        Assert.Equal("Human", await ModeAsync(conversationId));
    }

    [Fact]
    public async Task Concurrent_mode_changes_and_inbound_turns_complete_without_deadlock()
    {
        ConversationDoubles doubles = null!;
        await using var host = StartHost(d => doubles = d);
        var conversationId = await CreateConversationAsync(host);

        var work = new List<Task>();

        for (var index = 0; index < 3; index++)
        {
            work.Add(ChangeModeAsync(host, conversationId, index));
            work.Add(ProcessAsync(host, $"wamid.deadlock-{index}"));
        }

        // Every operation of the conversation has to finish; a lock that is never released, or a lock
        // taken in an inconsistent order, would leave this waiting until the assertion times out.
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task ChangeModeAsync(
        ConversationsHost host,
        long conversationId,
        int index)
    {
        await using var scope = host.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<IConversationModeControl>();

        _ = (index % 3) switch
        {
            0 => await control.TakeOverAsync(conversationId),
            1 => await control.ReleaseToAiAsync(conversationId),
            _ => await control.CloseAsync(conversationId),
        };
    }

    /// <summary>A host whose deterministic seams answer a greeting, observed by the test.</summary>
    private ConversationsHost StartHost(Action<ConversationDoubles> observe) =>
        ConversationsHost.Start(
            connectionString,
            services =>
            {
                var doubles = services.AddConversationDoubles(
                    NluAnalysisResult.Success(NluInterpretationWith(NluIntent.Greeting)));

                observe(doubles);
            });

    /// <summary>One plain automatic turn, which creates the customer's conversation on first use.</summary>
    private static async Task<long> CreateConversationAsync(ConversationsHost host) =>
        (await ProcessAsync(host, "wamid.race-first")).ConversationId;

    private Task<string> ModeAsync(long conversationId) =>
        catalog.ScalarAsync($"SELECT mode FROM conversations.conversation WHERE id = {conversationId}");

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private static NluInterpretation NluInterpretationWith(NluIntent intent) => new()
    {
        Intent = intent,
        RequiredPorts = [],
        Grades = [],
        BudgetType = NluBudgetType.None,
    };

    private static async Task<ConversationTurnResult> ProcessAsync(
        ConversationsHost host,
        string providerMessageId)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                Customer,
                "text",
                ConversationsTestDoubles.Now,
                "عندك ديل 24؟"));
    }
}
