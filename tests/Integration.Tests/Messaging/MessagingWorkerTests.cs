using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: one worker instance drains the durable queues through the claim operations,
/// and a worker whose handler does not exist yet leaves durable work untouched instead of stranding it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MessagingWorkerTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task The_inbox_worker_processes_a_claimed_message_through_the_processor()
    {
        var processor = new RecordingInboundProcessor();
        var id = await EnqueueInboundAsync("wamid.worker-1", "20100001101");

        await using var host = StartHost(services => services.AddSingleton<IInboundMessageProcessor>(processor));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<InboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal(id, Assert.Single(processor.Processed).Id);
        Assert.Equal("Processed", await InboxStatusAsync(id));

        // Nothing is left for the next poll.
        Assert.Equal(0, await worker.ProcessOnceAsync());
    }

    [Fact]
    public async Task The_inbox_worker_records_a_processing_failure_instead_of_losing_the_message()
    {
        var processor = new RecordingInboundProcessor
        {
            Failure = new InvalidOperationException("orchestration exploded"),
        };
        var id = await EnqueueInboundAsync("wamid.worker-2", "20100001102");

        await using var host = StartHost(services => services.AddSingleton<IInboundMessageProcessor>(processor));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<InboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await InboxStatusAsync(id));
        Assert.Equal("orchestration exploded", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Single(processor.Processed);
    }

    [Fact]
    public async Task The_outbox_worker_sends_durable_intent_through_the_sender()
    {
        var sender = new RecordingOutboundSender();
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(sender));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        var sent = Assert.Single(sender.Sent);

        Assert.Equal(id, sent.Id);
        Assert.Equal("the reply", sent.Body);
        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal($"wamid.sent.{id}", await Catalog.ScalarAsync(
            $"SELECT provider_message_id FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task The_outbox_worker_records_a_provider_failure()
    {
        var sender = new RecordingOutboundSender
        {
            Result = _ => OutboundSendResult.Failed("Meta returned 429"),
        };
        var id = await EnqueueOutboundAsync();

        await using var host = StartHost(services => services.AddSingleton<IOutboundMessageSender>(sender));
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal("Failed", await OutboxStatusAsync(id));
        Assert.Equal("Meta returned 429", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NULL"));
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
            outboxId = await outbound.EnqueueAsync(MessagingSamples.Outbound(conversationId: 13, customerExternalId: "20100001002"));
        }

        await using var workerScope = host.CreateScope();

        Assert.Equal(0, await workerScope.ServiceProvider.GetRequiredService<InboxWorker>().ProcessOnceAsync());
        Assert.Equal(0, await workerScope.ServiceProvider.GetRequiredService<OutboxWorker>().ProcessOnceAsync());

        Assert.Equal("Pending", await InboxStatusAsync(inboxId));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {inboxId}"));
        Assert.Equal("Pending", await OutboxStatusAsync(outboxId));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.outbox_message WHERE id = {outboxId}"));
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

        return await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: 11,
            customerExternalId: "20100000701",
            body: "the reply"));
    }

    private MessagingHost StartHost(Action<IServiceCollection> configureServices) =>
        MessagingHost.Start(ConnectionString, configure: null, configureServices);
}
