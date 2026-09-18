using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 10: the configured batch is spent on claimable partitions, not on rows that the
/// one-message-per-partition rule discards afterwards. A hot partition costs one slot, and the rest
/// of the batch goes to the independent partitions that are waiting behind it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HotPartitionBatchTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    public static TheoryData<QueueKind> Queues => new() { QueueKind.Inbox, QueueKind.Outbox };

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_hot_partition_costs_one_slot_of_the_batch(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long oldestOfHotPartition;
        long blockedOfHotPartition;
        long alsoBlockedOfHotPartition;
        long[] independent;

        await using (var scope = host.CreateScope())
        {
            // The three oldest messages all belong to one partition; the eligible messages of three
            // other partitions are newer.
            oldestOfHotPartition = await EnqueueAsync(queue, scope.ServiceProvider, "20100005001", "hot-1");
            blockedOfHotPartition = await EnqueueAsync(queue, scope.ServiceProvider, "20100005001", "hot-2");
            alsoBlockedOfHotPartition = await EnqueueAsync(queue, scope.ServiceProvider, "20100005001", "hot-3");

            independent =
            [
                await EnqueueAsync(queue, scope.ServiceProvider, "20100005002", "hot-4"),
                await EnqueueAsync(queue, scope.ServiceProvider, "20100005003", "hot-5"),
                await EnqueueAsync(queue, scope.ServiceProvider, "20100005004", "hot-6"),
            ];
        }

        await using var claimScope = host.CreateScope();
        var claimed = await ClaimAsync(queue, claimScope.ServiceProvider, batchSize: 4);

        // The oldest message of the hot partition and the three independent partitions fill the
        // batch, even though the three oldest rows of the queue belong to the same partition.
        long[] expected = [oldestOfHotPartition, .. independent];

        Assert.Equal(expected, claimed.Select(claim => claim.Id));
        Assert.Equal(4, claimed.Count);

        // The hot partition never contributes a second message to one batch.
        Assert.DoesNotContain(claimed, claim => claim.Id == blockedOfHotPartition);
        Assert.DoesNotContain(claimed, claim => claim.Id == alsoBlockedOfHotPartition);
        Assert.Equal("Pending", await StatusAsync(queue, blockedOfHotPartition));
        Assert.Equal("Pending", await StatusAsync(queue, alsoBlockedOfHotPartition));
        Assert.Equal("0", await AttemptsAsync(queue, blockedOfHotPartition));
        Assert.Equal("0", await AttemptsAsync(queue, alsoBlockedOfHotPartition));
    }

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_small_batch_is_filled_by_distinct_partitions(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstOfHotPartition;
        long secondOfHotPartition;
        long independent;

        await using (var scope = host.CreateScope())
        {
            firstOfHotPartition = await EnqueueAsync(queue, scope.ServiceProvider, "20100005101", "small-1");
            secondOfHotPartition = await EnqueueAsync(queue, scope.ServiceProvider, "20100005101", "small-2");
            independent = await EnqueueAsync(queue, scope.ServiceProvider, "20100005102", "small-3");
        }

        await using var claimScope = host.CreateScope();
        var claimed = await ClaimAsync(queue, claimScope.ServiceProvider, batchSize: 2);

        // A batch of two is two partitions, never two messages of the same partition.
        Assert.Equal(new[] { firstOfHotPartition, independent }, claimed.Select(claim => claim.Id));
        Assert.Equal("Pending", await StatusAsync(queue, secondOfHotPartition));
    }
}
