using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: outbound intent is durable before the send, delivery bookkeeping never
/// mutates the content, and a successful send is recorded as Sent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxQueueTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    private const string ReplyBody = "the reply";

    [Fact]
    public async Task Outbound_intent_is_durable_before_any_send_is_attempted()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host);

        Assert.True(id > 0);
        Assert.Equal("Pending", await OutboxStatusAsync(id));
        Assert.Equal(ReplyBody, await Catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("0", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NULL"));

        // The documented body-hash constraint holds, so the stored content is exactly the intent.
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ReplyBody)));

        Assert.Equal(expectedHash, await Catalog.ScalarAsync(
            $"SELECT encode(body_hash, 'hex') FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task The_stored_row_keeps_the_correlation_the_partition_and_the_attempt_limit()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host);

        Assert.Equal("corr-11", await Catalog.ScalarAsync(
            $"SELECT correlation_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("20100000701", await Catalog.ScalarAsync(
            $"SELECT customer_external_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("11", await Catalog.ScalarAsync(
            $"SELECT partition_key FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("AI", await Catalog.ScalarAsync(
            $"SELECT sender FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("5", await Catalog.ScalarAsync(
            $"SELECT max_attempts FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task Concurrent_claimers_never_claim_the_same_outbox_message()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var expectedIds = new List<long>(6);

        await using (var scope = host.CreateScope())
        {
            var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

            for (var index = 1; index <= 6; index++)
            {
                expectedIds.Add(await outbound.EnqueueAsync(
                    MessagingSamples.Outbound(conversationId: 100 + index, customerExternalId: $"2010000090{index}")));
            }
        }

        await using var firstScope = host.CreateScope();
        await using var secondScope = host.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
        var second = secondScope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        var claims = await Task.WhenAll(first.ClaimAsync(6), second.ClaimAsync(6));
        var claimedIds = claims.SelectMany(batch => batch.Select(message => message.Id)).ToList();

        Assert.Equal(expectedIds.Count, claimedIds.Count);
        Assert.Equal(expectedIds.Count, claimedIds.Distinct().Count());
        Assert.Equal(expectedIds.Order(), claimedIds.Order());
    }

    [Fact]
    public async Task A_successful_send_is_recorded_as_sent_with_the_provider_message_id()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host);

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        var claimed = Assert.Single(await store.ClaimAsync(10));

        Assert.Equal(id, claimed.Id);
        Assert.Equal(1, claimed.Attempts);
        Assert.Equal(5, claimed.MaxAttempts);
        Assert.Equal(ReplyBody, claimed.Body);
        Assert.Equal("corr-11", claimed.CorrelationId);
        Assert.Null(claimed.ProviderMessageId);
        Assert.Equal($"outbox:{id}", claimed.DeliveryKey);

        await store.CompleteAsync(claimed.Id, claimed.ClaimToken, "wamid.sent-1");

        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal("wamid.sent-1", await Catalog.ScalarAsync(
            $"SELECT provider_message_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("1", await Catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE id = {id} AND sent_at IS NOT NULL"));
        Assert.Empty(await store.ClaimAsync(10));
    }

    [Fact]
    public async Task Delivery_bookkeeping_never_mutates_the_stored_content()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host);

        var storedBody = await Catalog.ScalarAsync($"SELECT body FROM messaging.outbox_message WHERE id = {id}");
        var storedHash = await Catalog.ScalarAsync($"SELECT encode(body_hash, 'hex') FROM messaging.outbox_message WHERE id = {id}");

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        var claimed = Assert.Single(await store.ClaimAsync(10));

        Assert.Equal(id, claimed.Id);
        Assert.Equal(
            QueueFailureOutcome.RetryScheduled,
            await store.FailAsync(claimed.Id, claimed.ClaimToken, "Meta returned 500"));

        await MakeOutboxDueAsync(id);

        var retried = Assert.Single(await store.ClaimAsync(10));

        Assert.Equal(id, retried.Id);
        await store.CompleteAsync(retried.Id, retried.ClaimToken, "wamid.sent-2");

        Assert.Equal("Sent", await OutboxStatusAsync(id));
        Assert.Equal(storedBody, await Catalog.ScalarAsync($"SELECT body FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal(storedHash, await Catalog.ScalarAsync($"SELECT encode(body_hash, 'hex') FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("corr-11", await Catalog.ScalarAsync(
            $"SELECT correlation_id FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("Meta returned 500", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
    }

    [Fact]
    public async Task The_stored_attempt_limit_dead_letters_the_message()
    {
        await using var host = MessagingHost.Start(ConnectionString);
        var id = await EnqueueAsync(host);

        await using var scope = host.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var claimed = Assert.Single(await store.ClaimAsync(10));

            Assert.Equal(attempt, claimed.Attempts);
            Assert.Equal(5, claimed.MaxAttempts);

            var outcome = await store.FailAsync(claimed.Id, claimed.ClaimToken, $"failure {attempt}");

            Assert.Equal(
                attempt < 5 ? QueueFailureOutcome.RetryScheduled : QueueFailureOutcome.DeadLettered,
                outcome);

            await MakeOutboxDueAsync(id);
        }

        Assert.Equal("DeadLettered", await OutboxStatusAsync(id));
        Assert.Equal("5", await Catalog.ScalarAsync(
            $"SELECT attempts FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Equal("failure 5", await Catalog.ScalarAsync(
            $"SELECT last_error FROM messaging.outbox_message WHERE id = {id}"));
        Assert.Empty(await store.ClaimAsync(10));
    }

    private static async Task<long> EnqueueAsync(MessagingHost host)
    {
        await using var scope = host.CreateScope();
        var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

        return await outbound.EnqueueAsync(MessagingSamples.Outbound(
            conversationId: 11,
            customerExternalId: "20100000701",
            body: ReplyBody));
    }
}
