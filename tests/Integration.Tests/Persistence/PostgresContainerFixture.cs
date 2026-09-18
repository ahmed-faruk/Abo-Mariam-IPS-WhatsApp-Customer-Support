using Npgsql;
using Testcontainers.PostgreSql;

namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>
/// One PostgreSQL 18 container shared by the persistence suite. Tests get a throwaway database
/// inside it so every one of them starts from a genuinely empty database.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // The persistence suite creates one throwaway database per test, and each database keeps its
        // own pooled connections for the length of the run, so the shared container needs headroom
        // over the default max_connections of 100.
        container = new PostgreSqlBuilder("postgres:18")
            .WithCommand("-c", "max_connections=250")
            .Build();

        await container.StartAsync();

        ConnectionString = container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"monitor_test_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateEmptyDatabaseAsync();

        await ModulePersistence.MigrateAllAsync(connectionString);

        return connectionString;
    }
}

/// <summary>Shares the PostgreSQL container across every persistence test class.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "postgres-persistence";
}
