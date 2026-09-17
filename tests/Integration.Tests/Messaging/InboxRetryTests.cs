using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: a failed attempt records its error, schedules the retry and dead-letters at
/// the configured limit. TECHNICAL.md defines no attempt column for the Inbox, so the limit is policy.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InboxRetryTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    [Fact]
    public async Task A_failure_records_the_error_and_schedules_the_retry()
    {
        await using var host = MessagingHost.Start(
            ConnectionString,
            options =>
            {
                options.InboxMaxAttempts = 3;
                options.InboxRetryDelay = TimeSpan.FromMinutes(5);
            });

        var id = await EnqueueAsync(host, "wamid.retry-1", "20100000601");

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        Assert.Equal(1, Assert.Single(await store.ClaimAsync(10)).Attempts);
        Assert.Equal(QueueFailureOutcome.RetryScheduled, await store.FailAsync(id, "the processor crashed"));

        Assert.Equal("Failed", await InboxStatusAsync(id));
        Assert.Equal("1", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("the processor crashed", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.inbox_message WHERE id = {id}"));

        // The retry landed five minutes out, so the partition stays quiet until it is due.
        Assert.Equal("1", await Catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message "
            + $"WHERE id = {id} AND run_after >= now() + interval '4 minutes'"));
        Assert.Empty(await store.ClaimAsync(10));

        await MakeInboxDueAsync(id);

        var retried = Assert.Single(await store.ClaimAsync(10));

        Assert.Equal(id, retried.Id);
        Assert.Equal(2, retried.Attempts);
        Assert.Equal("Claimed", await InboxStatusAsync(id));
    }

    [Fact]
    public async Task The_configured_attempt_limit_dead_letters_the_message()
    {
        await using var host = MessagingHost.Start(
            ConnectionString,
            options =>
            {
                options.InboxMaxAttempts = 2;
                options.InboxRetryDelay = TimeSpan.FromSeconds(30);
            });

        var id = await EnqueueAsync(host, "wamid.limit-1", "20100000602");

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        Assert.Equal(1, Assert.Single(await store.ClaimAsync(10)).Attempts);
        Assert.Equal(QueueFailureOutcome.RetryScheduled, await store.FailAsync(id, "first failure"));

        await MakeInboxDueAsync(id);

        Assert.Equal(2, Assert.Single(await store.ClaimAsync(10)).Attempts);
        Assert.Equal(QueueFailureOutcome.DeadLettered, await store.FailAsync(id, "second failure"));

        Assert.Equal("DeadLettered", await InboxStatusAsync(id));
        Assert.Equal("2", await Catalog.ScalarAsync($"SELECT attempts FROM messaging.inbox_message WHERE id = {id}"));
        Assert.Equal("second failure", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.inbox_message WHERE id = {id}"));

        // A terminal failure is never claimed again, whatever the schedule says.
        await MakeInboxDueAsync(id);

        Assert.Empty(await store.ClaimAsync(10));
    }

    [Fact]
    public async Task A_completed_message_is_processed_and_never_claimed_again()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host, "wamid.complete-1", "20100000603");

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxMessageStore>();

        Assert.Equal(id, Assert.Single(await store.ClaimAsync(10)).Id);
        await store.CompleteAsync(id);

        Assert.Equal("Processed", await InboxStatusAsync(id));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.inbox_message WHERE id = {id} AND processed_at IS NOT NULL"));
        Assert.Empty(await store.ClaimAsync(10));
    }

    private static async Task<long> EnqueueAsync(MessagingHost host, string providerMessageId, string customer)
    {
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        return (await inbound.EnqueueAsync(MessagingSamples.Inbound(providerMessageId, customer))).InboxMessageId;
    }
}
