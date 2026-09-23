using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #5 acceptance: the inbound webhook transaction is durable and idempotent, so a repeated
/// provider delivery can never produce a second reply. See docs/TECHNICAL.md section 14.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InboundDurabilityTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Accepted_inbound_message_is_committed_before_the_caller_is_told()
    {
        var envelope = MessagingSamples.Inbound("wamid.dur-1", "20100000101");

        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        var result = await inbound.EnqueueAsync(envelope);

        Assert.False(result.IsDuplicate);
        Assert.True(result.InboxMessageId > 0);

        // The reader uses its own connection, so the row can only be visible after a commit.
        Assert.Equal("1", await catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.inbox_message WHERE id = {result.InboxMessageId}"));
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.webhook_envelope "
            + "WHERE raw_body = '{\"id\":\"wamid.dur-1\", \"from\": \"20100000101\", \"text\": \"hello\" }'::jsonb"));
        Assert.Equal("Pending", await catalog.ScalarAsync(
            $"SELECT processing_status FROM messaging.inbox_message WHERE id = {result.InboxMessageId}"));
        Assert.Equal("20100000101", await catalog.ScalarAsync(
            $"SELECT partition_key FROM messaging.inbox_message WHERE id = {result.InboxMessageId}"));
    }

    [Fact]
    public async Task Repeated_delivery_of_the_same_provider_message_stays_one_durable_row()
    {
        var envelope = MessagingSamples.Inbound("wamid.dur-2", "20100000102");

        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        var first = await inbound.EnqueueAsync(envelope);
        var second = await inbound.EnqueueAsync(envelope);
        var third = await inbound.EnqueueAsync(envelope);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.True(third.IsDuplicate);
        Assert.Equal(first.InboxMessageId, second.InboxMessageId);

        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.dur-2'"));
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task Duplicate_provider_message_id_with_a_new_body_adds_nothing()
    {
        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();

        var first = await inbound.EnqueueAsync(
            new InboundMessageEnvelope(
                RawBody: "{\"id\":\"wamid.dur-3\",\"text\":\"first\"}",
                ProviderMessageId: "wamid.dur-3",
                CustomerExternalId: "20100000103",
                MessageType: "text",
                ProviderTimestamp: MessagingSamples.ProviderTimestamp,
                Body: "first"));

        var second = await inbound.EnqueueAsync(
            new InboundMessageEnvelope(
                RawBody: "{\"id\":\"wamid.dur-3\",\"text\":\"second\"}",
                ProviderMessageId: "wamid.dur-3",
                CustomerExternalId: "20100000103",
                MessageType: "text",
                ProviderTimestamp: MessagingSamples.ProviderTimestamp,
                Body: "second"));

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.InboxMessageId, second.InboxMessageId);

        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(*) FROM messaging.inbox_message WHERE provider_message_id = 'wamid.dur-3'"));

        // The duplicate delivery is a complete no-op, so the unmatched envelope was not left behind.
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task One_webhook_envelope_can_anchor_multiple_customer_messages()
    {
        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();
        const string rawBody = """{"entry":[{"changes":[{"value":{"messages":[{"id":"wamid.multi-a"},{"id":"wamid.multi-b"}]}}]}]}""";

        var first = await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.multi-a",
            "20100003001",
            "text",
            MessagingSamples.ProviderTimestamp,
            "one"));
        var second = await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.multi-b",
            "20100003001",
            "text",
            MessagingSamples.ProviderTimestamp.AddSeconds(1),
            "two"));

        Assert.NotEqual(first.InboxMessageId, second.InboxMessageId);
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
        Assert.Equal("1", await catalog.ScalarAsync(
            "SELECT count(DISTINCT envelope_id) FROM messaging.inbox_message"));
    }

    [Fact]
    public async Task A_partial_multi_message_retry_deduplicates_the_prefix_and_accepts_the_missing_message()
    {
        await using var host = MessagingHost.Start(connectionString);
        await using var scope = host.CreateScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();
        const string rawBody = """{"entry":[{"changes":[{"value":{"messages":[{"id":"wamid.retry-a"},{"id":"wamid.retry-b"}]}}]}]}""";

        var first = await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.retry-a",
            "20100003002",
            "text",
            MessagingSamples.ProviderTimestamp,
            "one"));
        var duplicate = await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.retry-a",
            "20100003002",
            "text",
            MessagingSamples.ProviderTimestamp,
            "one"));
        var second = await inbound.EnqueueAsync(new InboundMessageEnvelope(
            rawBody,
            "wamid.retry-b",
            "20100003002",
            "text",
            MessagingSamples.ProviderTimestamp.AddSeconds(1),
            "two"));

        Assert.Equal(first.InboxMessageId, duplicate.InboxMessageId);
        Assert.True(duplicate.IsDuplicate);
        Assert.False(second.IsDuplicate);
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.inbox_message"));
    }
}
