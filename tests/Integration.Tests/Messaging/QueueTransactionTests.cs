using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: the transaction boundaries are real. A statement that fails inside the
/// acceptance or the claim leaves no partial durable state behind.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class QueueTransactionTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task A_failed_inbound_acceptance_stores_neither_the_envelope_nor_the_inbox_row()
    {
        // The envelope insert succeeds and the Inbox insert is rejected, so only a real
        // transaction can keep the envelope out of the database.
        await Catalog.ExecuteAsync(
            "ALTER TABLE messaging.inbox_message ADD CONSTRAINT ck_test_reject_provider "
            + "CHECK (provider_message_id <> 'wamid.blocked-1') NOT VALID");

        await using var host = MessagingHost.Start(ConnectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.blocked-1", "20100000801")));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("0", await Catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
        Assert.Equal("0", await Catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
    }

    [Fact]
    public async Task A_failed_claim_rolls_back_the_claim_state()
    {
        await Catalog.ExecuteAsync(
            "ALTER TABLE messaging.inbox_message ADD CONSTRAINT ck_test_reject_claim "
            + "CHECK (processing_status <> 'Claimed') NOT VALID");

        await using var host = MessagingHost.Start(ConnectionString);
        long id;

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            id = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.rollback-1", "20100000802"))).InboxMessageId;
        }

        await using var claimScope = host.CreateScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.ClaimAsync(10));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("Pending", await InboxStatusAsync(id));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.inbox_message WHERE id = {id} AND claimed_at IS NULL"));
    }

    [Fact]
    public async Task A_failed_outbox_claim_rolls_back_the_claim_state()
    {
        await Catalog.ExecuteAsync(
            "ALTER TABLE messaging.outbox_message ADD CONSTRAINT ck_test_reject_claim "
            + "CHECK (delivery_status <> 'Claimed') NOT VALID");

        await using var host = MessagingHost.Start(ConnectionString);
        long id;

        await using (var scope = host.CreateScope())
        {
            var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

            id = (await outbound.EnqueueAsync(
                MessagingSamples.Outbound(conversationId: 12, customerExternalId: "20100000803"))).OutboxMessageId;
        }

        await using var claimScope = host.CreateScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => store.ClaimAsync(10));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("Pending", await OutboxStatusAsync(id));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND claimed_at IS NULL"));
    }
}
