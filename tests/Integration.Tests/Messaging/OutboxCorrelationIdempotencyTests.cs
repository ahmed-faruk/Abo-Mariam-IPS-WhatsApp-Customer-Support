using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Issue #11 acceptance: the Outbox guarantees at most one durable reply per inbound turn. The
/// correlation id is the inbound provider message id, so a retried turn reuses the reply it already
/// stored instead of enqueuing a second one, and a concurrent duplicate converges on one row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxCorrelationIdempotencyTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Correlation = "wamid.HBgNMjAxMDAwMDAwMDE";

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_outbox_correlation_is_unique_in_the_database()
    {
        var indexes = await catalog.IndexesAsync();

        Assert.Contains(indexes, index =>
            index.Name == "ux_outbox_correlation_id"
            && index.Table == "messaging.outbox_message"
            && index.IsUnique
            && !index.IsPrimary
            && index.Definition.Contains("(correlation_id)", StringComparison.Ordinal));

        // The guarantee is the database's, not the application's: a manual duplicate insert fails.
        await catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, sender, body, body_hash, partition_key) "
            + "VALUES (81, '20100000811', 'wamid.manual', 'AI', 'a reply', "
            + "sha256(convert_to('a reply', 'UTF8')), '81')");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, sender, body, body_hash, partition_key) "
            + "VALUES (81, '20100000811', 'wamid.manual', 'AI', 'another reply', "
            + "sha256(convert_to('another reply', 'UTF8')), '81')"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_duplicate_enqueue_reuses_the_durable_row_instead_of_inserting_a_second_one()
    {
        await using var host = MessagingHost.Start(connectionString);

        var first = await EnqueueAsync(host, "the reply");
        var second = await EnqueueAsync(host, "the reply");

        Assert.Equal(first, second);
        Assert.Equal("1", await catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE correlation_id = '{Correlation}'"));
    }

    [Fact]
    public async Task A_duplicate_enqueue_never_replaces_the_originally_stored_reply()
    {
        await using var host = MessagingHost.Start(connectionString);

        var first = await EnqueueAsync(host, "the first accepted reply");
        var second = await EnqueueAsync(host, "a different reply that must not win");

        Assert.Equal(first, second);
        Assert.Equal("the first accepted reply", await catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE id = {first}"));
        Assert.Equal("1", await catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE correlation_id = '{Correlation}'"));
    }

    [Fact]
    public async Task Concurrent_duplicate_enqueues_converge_on_exactly_one_row()
    {
        await using var first = MessagingHost.Start(connectionString);
        await using var second = MessagingHost.Start(connectionString);
        await using var third = MessagingHost.Start(connectionString);

        var ids = await Task.WhenAll(
            EnqueueAsync(first, "the reply"),
            EnqueueAsync(second, "the reply"),
            EnqueueAsync(third, "the reply"));

        Assert.Single(ids.Distinct());
        Assert.Equal("1", await catalog.ScalarAsync(
            $"SELECT count(*) FROM messaging.outbox_message WHERE correlation_id = '{Correlation}'"));
        Assert.Equal("the reply", await catalog.ScalarAsync(
            $"SELECT body FROM messaging.outbox_message WHERE correlation_id = '{Correlation}'"));
    }

    [Fact]
    public async Task Different_correlations_still_enqueue_their_own_rows()
    {
        await using var host = MessagingHost.Start(connectionString);

        var first = await EnqueueAsync(host, "the first reply", "wamid.first");
        var second = await EnqueueAsync(host, "the second reply", "wamid.second");

        Assert.NotEqual(first, second);
        Assert.Equal("2", await catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message"));
    }

    [Fact]
    public async Task A_stored_reply_exposes_the_body_and_the_metadata_a_retry_has_to_reconcile()
    {
        await using var host = MessagingHost.Start(connectionString);

        var stored = await AcceptAsync(host, "the reply", metadata: """{"v":1}""");
        var acceptance = await FindAsync(host);

        Assert.NotNull(acceptance);
        Assert.Equal(stored.OutboxMessageId, acceptance.OutboxMessageId);
        Assert.Equal(81, acceptance.ConversationId);
        Assert.Equal("the reply", acceptance.Body);
        Assert.Equal("""{"v":1}""", acceptance.ApplicationMetadata);
        Assert.True(acceptance.IsExisting);
    }

    [Fact]
    public async Task A_duplicate_correlation_keeps_the_conversation_that_accepted_the_reply()
    {
        await using var host = MessagingHost.Start(connectionString);

        var first = await AcceptAsync(host, "the first accepted reply", metadata: """{"v":1}""");

        // A turn of another conversation replays the same correlation, and the acceptance it gets back has
        // to report the conversation that really owns the durable reply rather than the one that asked.
        var second = await AcceptAsync(
            host,
            "a reply of another conversation",
            metadata: """{"v":1,"human":true}""",
            conversationId: 82);

        Assert.Equal(first.OutboxMessageId, second.OutboxMessageId);
        Assert.Equal(81, second.ConversationId);
        Assert.True(second.IsExisting);
        Assert.Equal("the first accepted reply", second.Body);
        Assert.Equal("""{"v":1}""", second.ApplicationMetadata);
        Assert.Equal("1", await catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message"));
        Assert.Equal("81", await catalog.ScalarAsync(
            "SELECT conversation_id FROM messaging.outbox_message"));
    }

    [Fact]
    public async Task A_correlation_that_was_never_stored_is_absent()
    {
        await using var host = MessagingHost.Start(connectionString);

        Assert.Null(await FindAsync(host));
    }

    [Fact]
    public async Task A_duplicate_enqueue_never_replaces_the_originally_stored_metadata()
    {
        await using var host = MessagingHost.Start(connectionString);

        var first = await AcceptAsync(host, "the first accepted reply", metadata: """{"v":1,"displayed":[]}""");
        var second = await AcceptAsync(host, "a different reply", metadata: """{"v":1,"displayed":[{"m":9}]}""");

        Assert.Equal(first.OutboxMessageId, second.OutboxMessageId);
        Assert.True(second.IsExisting);
        Assert.Equal("the first accepted reply", second.Body);
        Assert.Equal("""{"v":1,"displayed":[]}""", second.ApplicationMetadata);
        Assert.Equal(
            """{"v":1,"displayed":[]}""",
            await catalog.ScalarAsync(
                $"SELECT application_metadata FROM messaging.outbox_message WHERE id = {first.OutboxMessageId}"));
    }

    [Fact]
    public async Task Application_metadata_longer_than_the_bounded_column_is_refused()
    {
        await using var host = MessagingHost.Start(connectionString);

        var oversized = new string('x', OutboundMessageRequest.MaxApplicationMetadataLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => EnqueueAsync(host, "the reply", metadata: oversized));
        Assert.Equal("0", await catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message"));
    }

    [Fact]
    public async Task The_stored_application_metadata_column_is_bounded_and_nullable()
    {
        var definition = await catalog.ScalarAsync(
            "SELECT data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'messaging' AND table_name = 'outbox_message' "
            + "AND column_name = 'application_metadata'");

        Assert.Equal(
            $"character varying|{OutboundMessageRequest.MaxApplicationMetadataLength}|YES",
            definition);
    }

    private static async Task<long> EnqueueAsync(
        MessagingHost host,
        string body,
        string correlationId = Correlation,
        string? metadata = null,
        long conversationId = 81)
    {
        await using var scope = host.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
            MessagingSamples.Outbound(
                conversationId: conversationId,
                customerExternalId: "20100000811",
                body: body,
                correlationId: correlationId,
                applicationMetadata: metadata))).OutboxMessageId;
    }

    private static async Task<OutboundAcceptance> AcceptAsync(
        MessagingHost host,
        string body,
        string correlationId = Correlation,
        string? metadata = null,
        long conversationId = 81)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
            MessagingSamples.Outbound(
                conversationId: conversationId,
                customerExternalId: "20100000811",
                body: body,
                correlationId: correlationId,
                applicationMetadata: metadata));
    }

    private static async Task<OutboundAcceptance?> FindAsync(
        MessagingHost host,
        string correlationId = Correlation)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>()
            .FindByCorrelationAsync(correlationId);
    }
}
