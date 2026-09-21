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

    private static async Task<long> EnqueueAsync(
        MessagingHost host,
        string body,
        string correlationId = Correlation)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
            MessagingSamples.Outbound(
                conversationId: 81,
                customerExternalId: "20100000811",
                body: body,
                correlationId: correlationId));
    }
}
