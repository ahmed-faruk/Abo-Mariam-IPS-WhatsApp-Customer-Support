using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: claims are exclusive, ordered per partition and horizontally safe, as
/// defined by the claim in docs/TECHNICAL.md section 15.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InboxClaimingTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task A_claim_takes_due_messages_in_id_order_and_holds_them_exclusively()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            firstId = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.claim-1", "20100000201"))).InboxMessageId;
            secondId = (await inbound.EnqueueAsync(
                MessagingSamples.Inbound("wamid.claim-2", "20100000202", conversationId: 77))).InboxMessageId;
        }

        await using var claimScope = host.CreateScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        var claimed = await store.ClaimAsync(10);

        Assert.Equal(new[] { firstId, secondId }, claimed.Select(message => message.Id));
        Assert.All(claimed, message => Assert.Equal(1, message.Attempts));

        Assert.Equal("hello", claimed[0].Body);
        Assert.Equal("text", claimed[0].MessageType);
        Assert.Equal("20100000201", claimed[0].CustomerExternalId);
        Assert.Equal(MessagingSamples.ProviderTimestamp, claimed[0].ProviderTimestamp);
        Assert.Null(claimed[0].ConversationId);
        Assert.Equal(77, claimed[1].ConversationId);

        // A claimed message is not claimable again, not even from a second worker connection.
        await using var otherScope = host.CreateScope();
        var otherStore = otherScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        Assert.Empty(await otherStore.ClaimAsync(10));
        Assert.Equal("Claimed", await InboxStatusAsync(firstId));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.inbox_message WHERE id = {firstId}"));
    }

    [Fact]
    public async Task Concurrent_claimers_never_claim_the_same_message()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var expectedIds = new List<long>(8);

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            for (var index = 0; index < 8; index++)
            {
                expectedIds.Add((await inbound.EnqueueAsync(
                    MessagingSamples.Inbound($"wamid.concurrent-{index}", $"2010000030{index}"))).InboxMessageId);
            }
        }

        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();
        var second = secondScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        var claims = await Task.WhenAll(first.ClaimAsync(8), second.ClaimAsync(8));
        var claimedIds = claims.SelectMany(batch => batch.Select(message => message.Id)).ToList();

        // FOR UPDATE SKIP LOCKED means each message lands in exactly one claimer's batch.
        Assert.Equal(expectedIds.Count, claimedIds.Count);
        Assert.Equal(expectedIds.Count, claimedIds.Distinct().Count());
        Assert.Equal(expectedIds.Order(), claimedIds.Order());
    }

    [Fact]
    public async Task Only_the_oldest_message_of_a_partition_is_claimed_at_a_time()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;
        long thirdId;

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            // Three messages from one customer arrive in order and share one partition.
            firstId = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.partition-1", "20100000401"))).InboxMessageId;
            secondId = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.partition-2", "20100000401"))).InboxMessageId;
            thirdId = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.partition-3", "20100000401"))).InboxMessageId;
        }

        await using var claimScope = host.CreateScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        var claimed = Assert.Single(await store.ClaimAsync(10));

        Assert.Equal(firstId, claimed.Id);
        Assert.Equal("20100000401", await Catalog.ScalarAsync(
            $"SELECT partition_key FROM messaging.inbox_message WHERE id = {firstId}"));

        // The partition is busy, so the later messages stay Pending rather than being claimed.
        Assert.Empty(await store.ClaimAsync(10));
        Assert.Equal("Pending", await InboxStatusAsync(secondId));
        Assert.Equal("Pending", await InboxStatusAsync(thirdId));

        await store.CompleteAsync(firstId);

        Assert.Equal(secondId, Assert.Single(await store.ClaimAsync(10)).Id);

        await store.CompleteAsync(secondId);

        Assert.Equal(thirdId, Assert.Single(await store.ClaimAsync(10)).Id);
    }

    [Fact]
    public async Task Future_run_after_is_not_claimable()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long id;

        await using (var scope = host.CreateScope())
        {
            var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

            id = (await inbound.EnqueueAsync(MessagingSamples.Inbound("wamid.future-1", "20100000501"))).InboxMessageId;
        }

        await Catalog.ExecuteAsync(
            $"UPDATE messaging.inbox_message SET run_after = now() + interval '1 hour' WHERE id = {id}");

        await using var claimScope = host.CreateScope();
        var store = claimScope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        Assert.Empty(await store.ClaimAsync(10));
        Assert.Equal("Pending", await InboxStatusAsync(id));
        Assert.Equal("0", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));

        await MakeInboxDueAsync(id);

        Assert.Equal(id, Assert.Single(await store.ClaimAsync(10)).Id);
    }
}
