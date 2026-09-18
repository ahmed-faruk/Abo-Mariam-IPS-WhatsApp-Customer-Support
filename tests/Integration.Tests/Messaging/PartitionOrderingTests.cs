using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 2: a later message may never overtake an earlier nonterminal message of the same
/// partition. Pending, Claimed and Failed all own the order - even while an earlier retry is still
/// scheduled in the future - and only a terminal message releases the ones behind it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartitionOrderingTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    public static TheoryData<QueueKind> Queues => new() { QueueKind.Inbox, QueueKind.Outbox };

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_message_waiting_for_its_retry_blocks_the_rest_of_its_partition(QueueKind queue)
    {
        await using var host = StartHost();

        long firstId;
        long secondId;
        long thirdId;

        await using (var scope = host.CreateScope())
        {
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100004001", "order-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100004001", "order-2");
            thirdId = await EnqueueAsync(queue, scope.ServiceProvider, "20100004001", "order-3");
        }

        await using var claimScope = host.CreateScope();
        var services = claimScope.ServiceProvider;

        // Only the oldest message of the partition is claimable, and it fails.
        var first = Assert.Single(await ClaimAsync(queue, services, 10));

        Assert.Equal(firstId, first.Id);
        Assert.Equal(
            QueueFailureOutcome.RetryScheduled,
            await FailAsync(queue, services, first, "the first attempt failed"));

        Assert.Equal("Failed", await StatusAsync(queue, firstId));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM {Table(queue)} WHERE id = {firstId} AND run_after > now()"));

        // The retry of the oldest message is still in the future, and that alone keeps every later
        // message of the partition out of the claim.
        Assert.Empty(await ClaimAsync(queue, services, 10));
        Assert.Equal("Pending", await StatusAsync(queue, secondId));
        Assert.Equal("Pending", await StatusAsync(queue, thirdId));
        Assert.Equal("0", await AttemptsAsync(queue, secondId));
        Assert.Equal("0", await AttemptsAsync(queue, thirdId));

        // When the retry becomes due the oldest message is claimed first, never a later one.
        await MakeDueAsync(queue, firstId);

        var retried = Assert.Single(await ClaimAsync(queue, services, 10));

        Assert.Equal(firstId, retried.Id);
        Assert.Equal(2, retried.Attempts);

        await CompleteAsync(queue, services, retried);

        Assert.Equal(TerminalStatus(queue), await StatusAsync(queue, firstId));

        // A terminal message releases the next message of the partition, and only that one.
        var second = Assert.Single(await ClaimAsync(queue, services, 10));

        Assert.Equal(secondId, second.Id);
        Assert.Equal("Pending", await StatusAsync(queue, thirdId));

        await CompleteAsync(queue, services, second);

        Assert.Equal(thirdId, Assert.Single(await ClaimAsync(queue, services, 10)).Id);
    }

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_blocking_message_that_is_not_due_yet_holds_its_partition(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;

        await using (var scope = host.CreateScope())
        {
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100004101", "future-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100004101", "future-2");
        }

        await Catalog.ExecuteAsync(
            $"UPDATE {Table(queue)} SET run_after = now() + interval '1 hour' WHERE id = {firstId}");

        await using var claimScope = host.CreateScope();
        var services = claimScope.ServiceProvider;

        // The oldest message of the partition is not due, and the message behind it must wait for it
        // instead of being claimed out of order.
        Assert.Empty(await ClaimAsync(queue, services, 10));
        Assert.Equal("Pending", await StatusAsync(queue, secondId));
        Assert.Equal("0", await AttemptsAsync(queue, secondId));

        await MakeDueAsync(queue, firstId);

        Assert.Equal(firstId, Assert.Single(await ClaimAsync(queue, services, 10)).Id);
        Assert.Equal("Pending", await StatusAsync(queue, secondId));
    }

    /// <summary>
    /// Both queues are configured with a retry delay far in the future, so a scheduled retry is
    /// never accidentally due while the test asserts the ordering it produces.
    /// </summary>
    private MessagingHost StartHost() =>
        MessagingHost.Start(
            ConnectionString,
            options =>
            {
                options.InboxMaxAttempts = 5;
                options.InboxRetryDelay = TimeSpan.FromHours(1);
                options.OutboxRetryDelay = TimeSpan.FromHours(1);
            });
}
