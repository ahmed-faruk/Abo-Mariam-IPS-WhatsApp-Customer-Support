using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 3: a send the provider accepted is never reported as a transport failure. A completion
/// that fails after a successful send is repaired by retrying the bookkeeping, and when the lease has
/// to recover the message the transport is asked for the same logical delivery through its stable
/// delivery key instead of delivering the reply a second time.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxDeliveryCompletionTests(PostgresContainerFixture postgres)
    : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task A_completion_failure_after_a_successful_send_is_retried_as_bookkeeping()
    {
        await InstallCompletionFaultAsync(failures: 1);

        var sender = new DeliveryLedgerSender();
        var id = await EnqueueAsync();
        var deliveryKey = $"outbox:{id}";

        await using var host = StartHost(sender);
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        // The provider accepted one logical delivery, and the worker recorded it by retrying the
        // completion of that delivery rather than by sending the reply again.
        Assert.Equal(new[] { deliveryKey }, sender.DeliveryKeys);
        Assert.Equal(1, sender.FreshDeliveries);
        Assert.Equal(0, sender.Reconciliations);

        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal($"wamid.{deliveryKey}", await Catalog.ScalarAsync(
            $"SELECT provider_message_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NOT NULL"));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(last_error, '') FROM messaging.outbox_message WHERE id = {id}"));

        // The injected fault really fired: the first completion was rejected and the retry landed,
        // so the database saw exactly two attempts to record the accepted delivery.
        Assert.Equal("2", await Catalog.ScalarAsync(
            "SELECT last_value FROM messaging.test_outbox_completion_failures"));
    }

    [Fact]
    public async Task An_accepted_delivery_whose_bookkeeping_failed_is_reconciled_after_the_lease_recovers_it()
    {
        // The bookkeeping keeps failing, exactly like an outage that outlives the worker's bounded
        // completion retries.
        await InstallCompletionFaultAsync(failures: 1000);

        var sender = new DeliveryLedgerSender();
        var id = await EnqueueAsync();
        var deliveryKey = $"outbox:{id}";

        await using var host = StartHost(sender);
        await using var scope = host.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        Assert.Equal(1, await worker.ProcessOnceAsync());

        // The provider accepted the delivery, so the attempt was never reported as a failed send:
        // the message stays claimed with no error until its lease is recovered.
        Assert.Equal(1, sender.FreshDeliveries);
        Assert.Equal(0, sender.Reconciliations);
        Assert.Equal("Claimed", await OutboxStatusAsync(id));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(provider_message_id, '') FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal(string.Empty, await Catalog.ScalarAsync(
            $"SELECT coalesce(last_error, '') FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));

        // The bookkeeping is retried a bounded number of times and never loops while it fails.
        Assert.Equal("3", await Catalog.ScalarAsync(
            "SELECT last_value FROM messaging.test_outbox_completion_failures"));

        // An immediate poll finds no work, so the reply is not delivered again while the first
        // accepted delivery is still owned.
        Assert.Equal(0, await worker.ProcessOnceAsync());
        Assert.Equal(1, sender.FreshDeliveries);
        Assert.Equal(0, sender.Reconciliations);

        // The bookkeeping is healthy again and the lease expires, so the recovered claim presents the
        // same delivery key: the transport reconciles instead of accepting a second delivery.
        await HealCompletionFaultAsync();
        await ExpireClaimAsync(QueueKind.Outbox, id);

        Assert.Equal(1, await worker.ProcessOnceAsync());

        Assert.Equal(new[] { deliveryKey }, sender.DeliveryKeys);
        Assert.Equal(1, sender.FreshDeliveries);
        Assert.Equal(1, sender.Reconciliations);
        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal(sender.AcceptedProviderMessageId(deliveryKey), await Catalog.ScalarAsync(
            $"SELECT provider_message_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("2", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        // The first poll spent the three bounded bookkeeping attempts on the fault; the reconciled
        // completion of the second poll was recorded with the fault removed.
        Assert.Equal("3", await Catalog.ScalarAsync(
            "SELECT last_value FROM messaging.test_outbox_completion_failures"));
    }

    private async Task<long> EnqueueAsync()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        await using var scope = host.CreateScope();
        var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

        return await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: 71,
            customerExternalId: "20100000711",
            body: "the accepted reply"));
    }

    private MessagingHost StartHost(IOutboundMessageSender sender) =>
        MessagingHost.Start(
            ConnectionString,
            configure: null,
            services => services.AddSingleton(sender));

    /// <summary>
    /// A real PostgreSQL fault at the completion boundary: the next <paramref name="failures"/> Sent
    /// updates of the Outbox fail, exactly like a lost acknowledgement or a connection that breaks
    /// after the provider already accepted the delivery. The counter is a sequence, so it survives
    /// the rollback of the statement it fails, which a table row would not.
    /// </summary>
    private Task InstallCompletionFaultAsync(int failures) => Catalog.ExecuteAsync(
        $"""
        CREATE SEQUENCE messaging.test_outbox_completion_failures;

        CREATE FUNCTION messaging.test_fail_sent_updates() RETURNS trigger LANGUAGE plpgsql AS $fault$
        BEGIN
            IF NEW.delivery_status <> 'Sent' THEN
                RETURN NEW;
            END IF;

            IF nextval('messaging.test_outbox_completion_failures') <= {failures} THEN
                RAISE EXCEPTION 'transient bookkeeping failure' USING ERRCODE = '08006';
            END IF;

            RETURN NEW;
        END
        $fault$;

        CREATE TRIGGER trg_test_fail_sent_updates
        BEFORE UPDATE ON messaging.outbox_message
        FOR EACH ROW EXECUTE FUNCTION messaging.test_fail_sent_updates();
        """);

    private Task HealCompletionFaultAsync() => Catalog.ExecuteAsync(
        "DROP TRIGGER trg_test_fail_sent_updates ON messaging.outbox_message");
}
