using WhatsAppMonitorAssistant.Integration.Tests.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Every queue test starts from a brand-new migrated PostgreSQL database, so no test can depend on
/// another test's durable state.
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

    /// <summary>Moves a scheduled retry into the past, the way waiting on the clock would.</summary>
    internal Task MakeInboxDueAsync(long inboxMessageId) => Catalog.ExecuteAsync(
        $"UPDATE messaging.inbox_message SET run_after = now() - interval '1 second' WHERE id = {inboxMessageId}");

    internal Task MakeOutboxDueAsync(long outboxMessageId) => Catalog.ExecuteAsync(
        $"UPDATE messaging.outbox_message SET run_after = now() - interval '1 second' WHERE id = {outboxMessageId}");

    internal Task<string> InboxStatusAsync(long inboxMessageId) => Catalog.ScalarAsync(
        $"SELECT processing_status FROM messaging.inbox_message WHERE id = {inboxMessageId}");

    internal Task<string> OutboxStatusAsync(long outboxMessageId) => Catalog.ScalarAsync(
        $"SELECT delivery_status FROM messaging.outbox_message WHERE id = {outboxMessageId}");
}
