using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Every queue test starts from a brand-new migrated PostgreSQL database, so no test can depend on
/// another test's durable state. The helpers below drive the two durable queues of
/// docs/TECHNICAL.md section 15 through their public contracts, so one regression can be asserted
/// for the Inbox and the Outbox the same way.
/// </summary>
public abstract class MessagingQueueFixture(PostgresContainerFixture postgres) : IAsyncLifetime
{
    internal string ConnectionString { get; private set; } = string.Empty;

    internal DatabaseCatalogReader Catalog { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = await postgres.CreateMigratedDatabaseAsync();
        Catalog = new DatabaseCatalogReader(ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    internal static string Table(QueueKind queue) =>
        queue == QueueKind.Inbox ? "messaging.inbox_message" : "messaging.outbox_message";

    internal static string StatusColumn(QueueKind queue) =>
        queue == QueueKind.Inbox ? "processing_status" : "delivery_status";

    /// <summary>The status a successfully completed message reaches: Inbox and Outbox differ here.</summary>
    internal static string TerminalStatus(QueueKind queue) =>
        queue == QueueKind.Inbox ? "Processed" : "Sent";

    /// <summary>
    /// Reads the claim statement the store itself runs, so an in-flight claim in a test is the real
    /// claim statement held open instead of a copy of it.
    /// </summary>
    internal static string ClaimSql(QueueKind queue) =>
        queue == QueueKind.Inbox ? InboxMessageStore.ClaimSql : OutboxMessageStore.ClaimSql;

    internal static async Task<long> EnqueueAsync(
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

        // Every enqueued reply of a test is a different logical reply, so it carries its own
        // correlation. The correlation id is what makes the Outbox enqueue idempotent, and the queue
        // tests below enqueue several replies into one partition to exercise ordering.
        return await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: long.Parse(partitionKey, CultureInfo.InvariantCulture),
            customerExternalId: $"20100{suffix}",
            correlationId: $"corr-{suffix}"));
    }

    internal static async Task<IReadOnlyList<QueueClaim>> ClaimAsync(
        QueueKind queue,
        IServiceProvider services,
        int batchSize)
    {
        if (queue == QueueKind.Inbox)
        {
            var inbox = services.GetRequiredService<IInboxMessageStore>();

            return
            [
                .. (await inbox.ClaimAsync(batchSize))
                    .Select(message => new QueueClaim(message.Id, message.ClaimToken, message.Attempts, string.Empty)),
            ];
        }

        var outbox = services.GetRequiredService<IOutboxMessageStore>();

        return
        [
            .. (await outbox.ClaimAsync(batchSize))
                .Select(message => new QueueClaim(message.Id, message.ClaimToken, message.Attempts, message.DeliveryKey)),
        ];
    }

    internal static Task CompleteAsync(QueueKind queue, IServiceProvider services, QueueClaim claim) =>
        CompleteWithTokenAsync(queue, services, claim.Id, claim.ClaimToken);

    internal static async Task CompleteWithTokenAsync(
        QueueKind queue,
        IServiceProvider services,
        long id,
        Guid claimToken)
    {
        if (queue == QueueKind.Inbox)
        {
            await services.GetRequiredService<IInboxMessageStore>().CompleteAsync(id, claimToken);

            return;
        }

        await services.GetRequiredService<IOutboxMessageStore>().CompleteAsync(id, claimToken, $"wamid.sent.{id}");
    }

    internal static Task<QueueFailureOutcome> FailAsync(
        QueueKind queue,
        IServiceProvider services,
        QueueClaim claim,
        string error) =>
        FailWithTokenAsync(queue, services, claim.Id, claim.ClaimToken, error);

    internal static Task<QueueFailureOutcome> FailWithTokenAsync(
        QueueKind queue,
        IServiceProvider services,
        long id,
        Guid claimToken,
        string error) =>
        queue == QueueKind.Inbox
            ? services.GetRequiredService<IInboxMessageStore>().FailAsync(id, claimToken, error)
            : services.GetRequiredService<IOutboxMessageStore>().FailAsync(id, claimToken, error);

    internal Task<string> StatusAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT {StatusColumn(queue)} FROM {Table(queue)} WHERE id = {id}");

    internal Task<string> AttemptsAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT attempts FROM {Table(queue)} WHERE id = {id}");

    internal Task<string> LastErrorAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT coalesce(last_error, '') FROM {Table(queue)} WHERE id = {id}");

    internal Task<string> ClaimTokenAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT coalesce(claim_token::text, '') FROM {Table(queue)} WHERE id = {id}");

    /// <summary>Proves the claim of a message is still leased instead of relying on the wall clock.</summary>
    internal Task<string> LeaseIsRunningAsync(QueueKind queue, long id) =>
        Catalog.ScalarAsync($"SELECT (claim_expires_at > now())::text FROM {Table(queue)} WHERE id = {id}");

    /// <summary>Ends a claim lease the way the clock would, without making the suite wait.</summary>
    internal Task ExpireClaimAsync(QueueKind queue, long id) =>
        Catalog.ExecuteAsync($"UPDATE {Table(queue)} SET claim_expires_at = now() - interval '1 second' WHERE id = {id}");

    /// <summary>Moves a scheduled retry into the past, the way waiting on the clock would.</summary>
    internal Task MakeDueAsync(QueueKind queue, long id) =>
        Catalog.ExecuteAsync($"UPDATE {Table(queue)} SET run_after = now() - interval '1 second' WHERE id = {id}");

    internal Task MakeInboxDueAsync(long inboxMessageId) => MakeDueAsync(QueueKind.Inbox, inboxMessageId);

    internal Task MakeOutboxDueAsync(long outboxMessageId) => MakeDueAsync(QueueKind.Outbox, outboxMessageId);

    internal Task<string> InboxStatusAsync(long inboxMessageId) => StatusAsync(QueueKind.Inbox, inboxMessageId);

    internal Task<string> OutboxStatusAsync(long outboxMessageId) => StatusAsync(QueueKind.Outbox, outboxMessageId);

    /// <summary>The durable queues the tests assert against. Each one owns its own claim statement.</summary>
    public enum QueueKind
    {
        Inbox,
        Outbox,
    }
}

/// <summary>
/// One claimed message, projected so an Inbox and an Outbox regression can be written the same way.
/// The delivery key only exists for the Outbox, where a delivery is identified across retries.
/// </summary>
internal sealed record QueueClaim(long Id, Guid ClaimToken, int Attempts, string DeliveryKey);
