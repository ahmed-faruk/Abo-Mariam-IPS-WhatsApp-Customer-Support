namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>
/// Issue #4 acceptance: schema-per-module ownership, one migration history per module and no
/// cross-module foreign keys, as defined in docs/TECHNICAL.md sections 4 and 6.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaOwnershipTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private static readonly string[] ExpectedTables =
    [
        "catalog.audit_log",
        "catalog.product_model",
        "catalog.product_model_port",
        "catalog.product_variant",
        "conversations.conversation",
        "conversations.conversation_state",
        "conversations.customer",
        "messaging.inbox_message",
        "messaging.outbox_message",
        "messaging.webhook_envelope",
        "storefront.business_info",
    ];

    private static readonly string[] ExpectedOwnedSchemas =
    [
        "catalog",
        "conversations",
        "identity",
        "messaging",
        "storefront",
    ];

    // Primary key column names as documented in docs/TECHNICAL.md section 6.
    private static readonly IReadOnlyDictionary<string, string> ExpectedPrimaryKeyColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["catalog.audit_log"] = "id",
            ["catalog.product_model"] = "id",
            ["catalog.product_model_port"] = "id",
            ["catalog.product_variant"] = "id",
            ["conversations.conversation"] = "id",
            ["conversations.conversation_state"] = "conversation_id",
            ["conversations.customer"] = "id",
            ["messaging.inbox_message"] = "id",
            ["messaging.outbox_message"] = "id",
            ["messaging.webhook_envelope"] = "id",
            ["storefront.business_info"] = "id",
        };

    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Every_module_owns_its_documented_schema()
    {
        var schemas = await catalog.SchemasAsync();

        Assert.All(ExpectedOwnedSchemas, schema => Assert.Contains(schema, schemas));
    }

    [Fact]
    public async Task Migrations_create_exactly_the_documented_baseline_tables()
    {
        var tables = await catalog.TablesAsync();

        var schemaTables = tables
            .Where(table => !table.EndsWith(".__ef_migrations", StringComparison.Ordinal))
            .OrderBy(table => table, StringComparer.Ordinal);

        Assert.Equal(ExpectedTables, schemaTables);
    }

    [Fact]
    public async Task Each_module_owns_its_own_migration_history()
    {
        var historyTables = await catalog.MigrationHistoryTablesAsync();

        Assert.Equal(
            ExpectedOwnedSchemas.Select(schema => $"{schema}.__ef_migrations").OrderBy(name => name, StringComparer.Ordinal),
            historyTables.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var schema in ExpectedOwnedSchemas)
        {
            var entries = await catalog.MigrationHistoryEntriesAsync(schema);

            Assert.True(
                entries.Count == 1,
                $"Expected one migration history row in schema '{schema}' but found {entries.Count}.");
        }
    }

    [Fact]
    public async Task Every_baseline_table_uses_its_documented_lowercase_primary_key_column()
    {
        var primaryKeys = await catalog.PrimaryKeyColumnsAsync();

        Assert.Equal(
            ExpectedPrimaryKeyColumns
                .Select(entry => $"{entry.Key} -> {entry.Value}")
                .OrderBy(description => description, StringComparer.Ordinal),
            primaryKeys
                .Where(key => !key.Table.EndsWith(".__ef_migrations", StringComparison.Ordinal))
                .Select(key => $"{key.Table} -> {key.Column}")
                .OrderBy(description => description, StringComparer.Ordinal));

        // A quoted mixed-case "Id" column would mean the PascalCase C# property leaked into the schema.
        var baselinePrimaryKeys = primaryKeys
            .Where(key => !key.Table.EndsWith(".__ef_migrations", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(baselinePrimaryKeys, key => key.Column != key.Column.ToLowerInvariant());
        Assert.DoesNotContain(baselinePrimaryKeys, key => string.Equals(key.Column, "Id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_foreign_key_crosses_a_module_schema()
    {
        var foreignKeys = await catalog.ForeignKeysAsync();

        Assert.NotEmpty(foreignKeys);

        var crossModule = foreignKeys
            .Where(key => !string.Equals(key.ChildSchema, key.ParentSchema, StringComparison.Ordinal))
            .Select(key => $"{key.ChildSchema}.{key.ChildTable} -> {key.ParentSchema}.{key.ParentTable} ({key.Name})")
            .ToList();

        Assert.Empty(crossModule);

        Assert.Equal(
            [
                "catalog.product_model_port -> catalog.product_model",
                "catalog.product_variant -> catalog.product_model",
                "conversations.conversation -> conversations.customer",
                "conversations.conversation_state -> conversations.conversation",
                "messaging.inbox_message -> messaging.webhook_envelope",
            ],
            foreignKeys
                .Select(key => $"{key.ChildSchema}.{key.ChildTable} -> {key.ParentSchema}.{key.ParentTable}")
                .OrderBy(description => description, StringComparer.Ordinal));
    }
}
