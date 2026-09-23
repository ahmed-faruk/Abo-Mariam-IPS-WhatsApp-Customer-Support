using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: one worker instance drains the durable queues through the claim operations,
/// and a worker whose handler does not exist yet leaves durable work untouched instead of stranding
/// it. Every outcome below is asserted from the durable queue rather than from how often a handler
/// was invoked, so the tests describe what the module stores, not how it happens to call a stub.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MessagingWorkerTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task The_inbox_worker_processes_a_claimed_message_to_a_terminal_state()
    {
        var id = await EnqueueInboundAsync("wamid.worker-1", "20100001101");

        await using var host = StartHost(
            services => services.AddSingleton<IInboundMessageProcessor>(new StubInboundProcessor()));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<InboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Processed", await InboxStatusAsync(id));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.inbox_message WHERE id = {id} AND processed_at IS NOT NULL"));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(last_error, '') FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(claim_token::text, '') FROM messaging.inbox_message WHERE id = {id}"));

        // Nothing is left for the next poll.
        Assert.Equal(0, await worker.ProcessOnceAsync());
    }

    [Fact]
    public async Task The_inbox_worker_records_a_processing_failure_instead_of_losing_the_message()
    {
        var id = await EnqueueInboundAsync("wamid.worker-2", "20100001102");

        await using var host = StartHost(services => services.AddSingleton<IInboundMessageProcessor>(
            new StubInboundProcessor { Failure = new InvalidOperationException("orchestration exploded") }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<InboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await InboxStatusAsync(id));
        Assert.Equal("Processing failed: InvalidOperationException", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));

        // The retry is scheduled, so the message is not offered to the worker again before it is due.
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.inbox_message WHERE id = {id} AND run_after > now()"));
        Assert.Equal(0, await worker.ProcessOnceAsync());
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task The_outbox_worker_records_an_accepted_delivery_as_sent()
    {
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(
            services => services.AddSingleton<IOutboundMessageSender>(new StubOutboundSender()));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal($"wamid.sent.{id}", await Catalog.ScalarAsync(
            $"SELECT provider_message_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("the reply", await Catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NOT NULL"));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(last_error, '') FROM messaging.outbox_message WHERE id = {id}"));

        Assert.Equal(0, await worker.ProcessOnceAsync());
    }

    [Fact]
    public async Task The_outbox_worker_schedules_a_retry_when_the_provider_refuses_the_send()
    {
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(
            new StubOutboundSender { Result = _ => OutboundSendResult.Failed("Meta returned 429") }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await OutboxStatusAsync(id));
        Assert.Equal("Meta returned 429", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.outbox_message "
            + $"WHERE id = {id} AND provider_message_id IS NULL AND sent_at IS NULL"));

        // A refused send is never recorded as a delivery, and the retry is not due yet.
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND run_after > now()"));
        Assert.Equal(0, await worker.ProcessOnceAsync());
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task The_outbox_worker_schedules_a_retry_when_the_transport_throws()
    {
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(
            new StubOutboundSender { Failure = new HttpRequestException("Meta is unreachable") }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await OutboxStatusAsync(id));
        Assert.Equal("Unknown provider outcome: HttpRequestException", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NULL"));
    }

    [Fact]
    public async Task An_unbounded_sender_exception_never_reaches_the_stored_diagnostic()
    {
        var id = await EnqueueOutboundAsync();
        var uncontrolledText = "customer 20100000001 said: " + new string('y', 100_000);

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(
            new StubOutboundSender { Failure = new HttpRequestException(uncontrolledText) }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        var stored = await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}");

        Assert.Equal("Unknown provider outcome: HttpRequestException", stored);
        Assert.DoesNotContain("20100000001", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("yyyy", stored, StringComparison.Ordinal);
        Assert.True(stored.Length < 100, $"The stored diagnostic was {stored.Length} characters.");
    }

    [Fact]
    public async Task The_outbox_worker_dead_letters_a_permanent_failure_without_spending_remaining_attempts()
    {
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(
            new StubOutboundSender { Result = _ => OutboundSendResult.PermanentFailure("Meta rejected unchanged request") }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("DeadLettered", await OutboxStatusAsync(id));
        Assert.Equal("Meta rejected unchanged request", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal(0, await worker.ProcessOnceAsync());
    }

    [Fact]
    public async Task The_outbox_worker_records_unknown_outcome_truthfully()
    {
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(
            new StubOutboundSender { Result = _ => OutboundSendResult.Unknown("Unknown provider outcome: timeout") }));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await OutboxStatusAsync(id));
        Assert.Equal("Unknown provider outcome: timeout", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task Workers_leave_durable_work_unclaimed_while_their_handlers_are_absent()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long inboxId;
        long outboxId;

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();
            var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

            inboxId = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.idle-1", "20100001001"))).InboxMessageId;
            outboxId = (await outbound.EnqueueAsync(
                MessagingSamples.Outbound(conversationId: 13, customerExternalId: "20100001002"))).OutboxMessageId;
        }

        await using var workerScope = host.CreateScope();

        Assert.Equal(0, await workerScope.ServiceProvider.GetRequiredService<InboxWorker>().ProcessOnceAsync());
        Assert.Equal(0, await workerScope.ServiceProvider.GetRequiredService<OutboxWorker>().ProcessOnceAsync());

        Assert.Equal("Pending", await InboxStatusAsync(inboxId));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {inboxId}"));
        Assert.Equal("Pending", await OutboxStatusAsync(outboxId));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.outbox_message WHERE id = {outboxId}"));
    }

    [Fact]
    public async Task The_outbox_worker_never_sends_on_a_claim_another_worker_could_already_reclaim()
    {
        long firstId;
        long secondId;

        await using (var host = MessagingHost.Start(ConnectionString))
        await using (var scope = host.CreateScope())
        {
            var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

            // One reply per partition, so a single claim batch would take both of them.
            firstId = (await outbound.EnqueueAsync(MessagingSamples.Outbound(
                conversationId: 41,
                customerExternalId: "20100000801",
                correlationId: "corr-lease-a"))).OutboxMessageId;
            secondId = (await outbound.EnqueueAsync(MessagingSamples.Outbound(
                conversationId: 42,
                customerExternalId: "20100000802",
                correlationId: "corr-lease-b"))).OutboxMessageId;
        }

        var sender = new LeaseObservingSender(Catalog);

        await using (var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(sender)))
        await using (var scope = host.CreateScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OutboxWorker>().ProcessOnceAsync());
        }

        // The first send outlives the lease of every other reply the poll had claimed. A reply that
        // had been claimed before its send would therefore leave the transport on a lease another
        // worker was already allowed to reclaim, which is exactly the duplicate send this guards.
        Assert.True(
            sender.EverySendHeldALiveLease,
            "An Outbox send began on a claim whose lease another worker could already reclaim.");
        Assert.Equal([firstId, secondId], sender.SentIds.Order());
        Assert.Equal("Sent", await OutboxStatusAsync(firstId));
        Assert.Equal("Sent", await OutboxStatusAsync(secondId));
    }

    private async Task<long> EnqueueInboundAsync(string providerMessageId, string customerExternalId)
    {
        await using var host = MessagingHost.Start(ConnectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        return (await inbound.EnqueueAsync(
            MessagingSamples.Inbound(providerMessageId, customerExternalId))).InboxMessageId;
    }

    private async Task<long> EnqueueOutboundAsync()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        await using var scope = host.CreateScope();
        var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

        return (await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: 11,
            customerExternalId: "20100000701",
            body: "the reply"))).OutboxMessageId;
    }

    private MessagingHost StartHost(Action<IServiceCollection> configureServices) =>
        MessagingHost.Start(ConnectionString, configure: null, configureServices);

    /// <summary>
    /// A sender that behaves like a real Meta round trip. Every send observes the durable claim it was
    /// handed, and a send that outlives the lease of the rest of the poll ends those leases the way
    /// the clock would.
    /// </summary>
    private sealed class LeaseObservingSender(DatabaseCatalogReader catalog) : IOutboundMessageSender
    {
        public List<long> SentIds { get; } = [];

        public bool EverySendHeldALiveLease { get; private set; } = true;

        public async Task<OutboundSendResult> SendAsync(
            ClaimedOutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            var lease = await catalog.ScalarAsync(
                "SELECT (delivery_status = 'Claimed' AND claim_expires_at > now())::text "
                + $"FROM messaging.outbox_message WHERE id = {message.Id}");

            EverySendHeldALiveLease &= lease == "true";
            SentIds.Add(message.Id);

            await catalog.ExecuteAsync(
                "UPDATE messaging.outbox_message SET claim_expires_at = now() - interval '1 second' "
                + $"WHERE delivery_status = 'Claimed' AND id <> {message.Id}");

            return OutboundSendResult.Sent($"wamid.sent.{message.Id}");
        }
    }
}
