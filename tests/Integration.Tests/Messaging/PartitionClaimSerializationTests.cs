using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: one partition is claimed by one claim transaction at a time. A claim that
/// has not committed yet is invisible to the <c>NOT EXISTS</c> of the claim statement and
/// <c>SKIP LOCKED</c> steps over the row that holds it, so the partition itself is locked for the
/// rest of the claiming transaction.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartitionClaimSerializationTests(PostgresContainerFixture postgres)
    : MessagingQueueFixture(postgres)
{
    /// <summary>
    /// The partition lock identity of the claim statements, spelled exactly as
    /// <c>InboxMessageStore.ClaimSql</c> and <c>OutboxMessageStore.ClaimSql</c> spell it. A
    /// transaction that holds it is in the middle of claiming that partition.
    /// </summary>
    private const string PartitionLockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@partition_key, 0))";

    public static TheoryData<QueueKind> Queues => new() { QueueKind.Inbox, QueueKind.Outbox };

    /// <summary>
    /// The regression for the finding: two messages of one partition are eligible, the first claim
    /// transaction is still open, and the second claimer must not take the second message.
    /// </summary>
    [Theory]
    [MemberData(nameof(Queues))]
    public async Task An_in_flight_claim_keeps_the_rest_of_its_partition_unclaimed(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;
        long otherPartitionId;

        await using (var scope = host.CreateScope())
        {
            // Two messages of one partition share it; the third one belongs to another partition.
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100001201", "serialize-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100001201", "serialize-2");
            otherPartitionId = await EnqueueAsync(queue, scope.ServiceProvider, "20100001202", "serialize-3");
        }

        await using var inFlightConnection = new NpgsqlConnection(ConnectionString);
        await inFlightConnection.OpenAsync();

        var inFlight = await inFlightConnection.BeginTransactionAsync();

        // The first claim is in flight. It runs the claim statement of the store itself, takes the
        // oldest message of the partition and keeps the partition until this transaction ends.
        Assert.Equal(new[] { firstId }, await ClaimInFlightAsync(inFlightConnection, inFlight, queue, batchSize: 1));

        await using (var scope = host.CreateScope())
        {
            // The second claimer cannot see the uncommitted claim, and the partition is locked for
            // the rest of the first claim, so only the untouched partition can move.
            Assert.Equal(new[] { otherPartitionId }, await ClaimAsync(queue, scope.ServiceProvider, 10));
            Assert.Equal("Pending", await StatusAsync(queue, secondId));
            Assert.Equal("0", await AttemptsAsync(queue, secondId));
        }

        await inFlight.RollbackAsync();
        await inFlight.DisposeAsync();

        await using (var scope = host.CreateScope())
        {
            // The in-flight claim never committed, so the partition is still claimed oldest first.
            Assert.Equal(firstId, Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10)));

            await CompleteAsync(queue, scope.ServiceProvider, firstId);

            Assert.Equal(secondId, Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10)));
        }
    }

    /// <summary>
    /// A partition held by another transaction cannot be claimed, while the rest of the queue still
    /// moves: the serialization is per partition and not a queue-wide lock.
    /// </summary>
    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_partition_held_by_another_transaction_is_left_alone(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long heldId;
        long otherPartitionId;

        await using (var scope = host.CreateScope())
        {
            // One message per partition, so nothing but the partition lock can keep the first one
            // back: there is no earlier message of the partition to wait behind.
            heldId = await EnqueueAsync(queue, scope.ServiceProvider, "20100001211", "held-1");
            otherPartitionId = await EnqueueAsync(queue, scope.ServiceProvider, "20100001212", "held-2");
        }

        await using var holderConnection = new NpgsqlConnection(ConnectionString);
        await holderConnection.OpenAsync();

        var holder = await holderConnection.BeginTransactionAsync();
        await ExecuteAsync(holderConnection, holder, PartitionLockSql, ("partition_key", "20100001211"));

        await using (var scope = host.CreateScope())
        {
            Assert.Equal(new[] { otherPartitionId }, await ClaimAsync(queue, scope.ServiceProvider, 10));
            Assert.Equal("Pending", await StatusAsync(queue, heldId));
            Assert.Equal("0", await AttemptsAsync(queue, heldId));
        }

        // The lock is transaction scoped, so ending the transaction releases the partition.
        await holder.RollbackAsync();
        await holder.DisposeAsync();

        await using (var scope = host.CreateScope())
        {
            Assert.Equal(heldId, Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10)));
        }
    }

    /// <summary>
    /// Concurrent claimers on one partition, claimed by real claim transactions rather than by a
    /// transaction the test holds open.
    /// </summary>
    [Theory]
    [MemberData(nameof(Queues))]
    public async Task Concurrent_claimers_never_take_two_messages_of_one_partition(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var madeProgress = false;

        for (var round = 0; round < 6; round++)
        {
            var partition = $"2010000130{round}";
            long firstId;
            long secondId;

            await using (var scope = host.CreateScope())
            {
                firstId = await EnqueueAsync(queue, scope.ServiceProvider, partition, $"round-{round}-1");
                secondId = await EnqueueAsync(queue, scope.ServiceProvider, partition, $"round-{round}-2");
            }

            await using var firstScope = host.CreateScope();
            await using var secondScope = host.CreateScope();

            var batches = await Task.WhenAll(
                ClaimAsync(queue, firstScope.ServiceProvider, 10),
                ClaimAsync(queue, secondScope.ServiceProvider, 10));
            var claimed = batches.SelectMany(batch => batch).ToList();

            // Two claim transactions may never take two messages of one partition, and the oldest
            // message of the partition is always the one that goes first.
            Assert.True(
                claimed.Count <= 1,
                $"Round {round} of partition {partition} claimed "
                + $"[{string.Join(" | ", batches.Select(batch => string.Join(",", batch)))}] "
                + $"while first={firstId} and second={secondId}.");
            Assert.All(claimed, id => Assert.Equal(firstId, id));
            Assert.Equal("Pending", await StatusAsync(queue, secondId));

            madeProgress |= claimed.Count == 1;
        }

        Assert.True(madeProgress, "No claim round made progress, so the partition was never claimed.");
    }

    /// <summary>
    /// The two durable queues of docs/TECHNICAL.md section 15. Each one owns its own claim
    /// statement, so every claim assertion is made against both of them.
    /// </summary>
    public enum QueueKind
    {
        Inbox,
        Outbox,
    }

    /// <summary>
    /// Reads the claim statement the store itself runs, so an in-flight claim in a test is the real
    /// claim statement held open instead of a copy of it.
    /// </summary>
    private static string ClaimSql(QueueKind queue) => (string)typeof(MessagingDbContext).Assembly
        .GetType(
            "WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence." + StoreTypeName(queue),
            throwOnError: true)!
        .GetField("ClaimSql", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetRawConstantValue()!;

    private static string StoreTypeName(QueueKind queue) =>
        queue == QueueKind.Inbox ? "InboxMessageStore" : "OutboxMessageStore";

    private static string Table(QueueKind queue) =>
        queue == QueueKind.Inbox ? "messaging.inbox_message" : "messaging.outbox_message";

    private static string StatusColumn(QueueKind queue) =>
        queue == QueueKind.Inbox ? "processing_status" : "delivery_status";

    private Task<string> StatusAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT {StatusColumn(queue)} FROM {Table(queue)} WHERE id = {id}");

    private Task<string> AttemptsAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT attempts FROM {Table(queue)} WHERE id = {id}");

    private static async Task<long> EnqueueAsync(
        QueueKind queue,
        IServiceProvider services,
        string partitionKey,
        string suffix)
    {
        if (queue == QueueKind.Inbox)
        {
            var inbound = services.GetRequiredService<IInboundMessageQueue>();

            return (await inbound.EnqueueAsync(MessagingSamples.Inbound($"wamid.{suffix}", partitionKey))).InboxMessageId;
        }

        var outbound = services.GetRequiredService<IOutboundMessageQueue>();

        return await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: long.Parse(partitionKey, CultureInfo.InvariantCulture),
            customerExternalId: $"20100{suffix}"));
    }

    private static async Task<IReadOnlyList<long>> ClaimAsync(
        QueueKind queue,
        IServiceProvider services,
        int batchSize)
    {
        if (queue == QueueKind.Inbox)
        {
            var inbox = services.GetRequiredService<IInboxMessageStore>();

            return [.. (await inbox.ClaimAsync(batchSize)).Select(message => message.Id)];
        }

        var outbox = services.GetRequiredService<IOutboxMessageStore>();

        return [.. (await outbox.ClaimAsync(batchSize)).Select(message => message.Id)];
    }

    private static async Task CompleteAsync(QueueKind queue, IServiceProvider services, long id)
    {
        if (queue == QueueKind.Inbox)
        {
            await services.GetRequiredService<IInboxMessageStore>().CompleteAsync(id);

            return;
        }

        await services.GetRequiredService<IOutboxMessageStore>().CompleteAsync(id, $"wamid.sent.{id}");
    }

    private static async Task<IReadOnlyList<long>> ClaimInFlightAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        QueueKind queue,
        int batchSize)
    {
        await using var command = new NpgsqlCommand(ClaimSql(queue), connection, transaction);
        command.Parameters.AddWithValue("batch_size", batchSize);

        var claimed = new List<long>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            claimed.Add(reader.GetInt64(0));
        }

        return claimed;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, string Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
