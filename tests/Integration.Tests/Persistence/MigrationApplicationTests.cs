namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>
/// Issue #4 acceptance: every module migration applies to an empty PostgreSQL database.
/// docs/TECHNICAL.md sections 6 and 7 define the expected schemas and migration history tables.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationApplicationTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedMigrations =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["CatalogDbContext"] = ["InitialCatalog"],
            ["ConversationDbContext"] = ["InitialConversations"],
            ["MessagingDbContext"] = ["InitialMessaging", "AddClaimLeasesAndUtf8BodyHash"],
            ["StorefrontDbContext"] = ["InitialStorefront"],
            ["IdentityDbContext"] = ["InitialIdentity"],
        };

    private string connectionString = string.Empty;
    private DatabaseCatalogReader catalog = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_database_has_no_module_schema_before_migrating()
    {
        var schemas = await catalog.SchemasAsync();

        Assert.DoesNotContain("catalog", schemas);
        Assert.DoesNotContain("conversations", schemas);
        Assert.DoesNotContain("messaging", schemas);
        Assert.DoesNotContain("storefront", schemas);
        Assert.DoesNotContain("identity", schemas);
    }

    [Fact]
    public async Task All_module_migrations_apply_to_an_empty_database()
    {
        await ModulePersistence.MigrateAllAsync(connectionString);

        var applied = await ModulePersistence.AppliedMigrationsAsync(connectionString);

        Assert.Equal(
            ExpectedMigrations.Keys.OrderBy(name => name, StringComparer.Ordinal),
            applied.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var (contextName, migrations) in applied)
        {
            // A migration id is "<timestamp>_<name>", and every module applies its migrations in
            // order, so a corrective migration is asserted here instead of being appended silently.
            Assert.Equal(ExpectedMigrations[contextName], migrations.Select(ToMigrationName));
        }
    }

    [Fact]
    public async Task Applying_all_migrations_twice_changes_nothing()
    {
        await ModulePersistence.MigrateAllAsync(connectionString);
        var firstPass = await catalog.MigrationHistoryEntriesAsync("catalog");

        await ModulePersistence.MigrateAllAsync(connectionString);
        var secondPass = await catalog.MigrationHistoryEntriesAsync("catalog");

        Assert.Equal(firstPass, secondPass);
        Assert.Single(secondPass);
    }

    private static string ToMigrationName(string migrationId) =>
        migrationId[(migrationId.IndexOf('_', StringComparison.Ordinal) + 1)..];
}
