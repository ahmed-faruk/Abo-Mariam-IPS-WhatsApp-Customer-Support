using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Every Storefront test starts from a brand-new migrated PostgreSQL database, so no test can depend on
/// another test's durable state. The helpers seed <c>storefront.business_info</c> directly, because demo
/// business information is data provisioning rather than part of Issue #7.
/// </summary>
public abstract class StorefrontFixture(PostgresContainerFixture postgres) : IAsyncLifetime
{
    internal string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    internal StorefrontHost StartHost() => StorefrontHost.Start(ConnectionString);

    internal static IStorefrontBusinessInfo BusinessInfo(IServiceProvider services) =>
        services.GetRequiredService<IStorefrontBusinessInfo>();

    internal static IStorefrontBusinessInfoUpdates Updates(IServiceProvider services) =>
        services.GetRequiredService<IStorefrontBusinessInfoUpdates>();

    /// <summary>Inserts one complete business-info row and returns its id.</summary>
    internal async Task<long> SeedAsync(string key, string answerAr, string? answerEn = null, bool isActive = true)
    {
        const string sql = """
            INSERT INTO storefront.business_info ("key", answer_ar, answer_en, is_active)
            VALUES (@key, @answer_ar, @answer_en, @is_active)
            RETURNING id;
            """;

        return (long)(await ScalarAsync(
            sql,
            ("key", key),
            ("answer_ar", answerAr),
            ("answer_en", answerEn),
            ("is_active", isActive)))!;
    }

    internal Task<IReadOnlyList<string>> BusinessInfoColumnsAsync() =>
        ColumnAsync(
            "SELECT column_name FROM information_schema.columns "
            + "WHERE table_schema = 'storefront' AND table_name = 'business_info' "
            + "ORDER BY ordinal_position");

    internal async Task<string?> StoredAnswerArAsync(string key) =>
        Text(await ScalarAsync(
            "SELECT answer_ar FROM storefront.business_info WHERE \"key\" = @key",
            ("key", key)));

    internal async Task<string?> StoredAnswerEnAsync(string key) =>
        Text(await ScalarAsync(
            "SELECT answer_en FROM storefront.business_info WHERE \"key\" = @key",
            ("key", key)));

    internal async Task<string?> StoredActiveStateAsync(string key) =>
        Text(await ScalarAsync(
            "SELECT is_active::text FROM storefront.business_info WHERE \"key\" = @key",
            ("key", key)));

    internal async Task<string?> StoredUpdatedAtAsync(string key) =>
        Text(await ScalarAsync(
            "SELECT updated_at::text FROM storefront.business_info WHERE \"key\" = @key",
            ("key", key)));

    /// <summary>Asks PostgreSQL whether the stored timestamp is exactly the given one.</summary>
    internal async Task<bool> StoredUpdatedAtIsAsync(string key, DateTime updatedAt) =>
        (bool)(await ScalarAsync(
            "SELECT (updated_at = @updated_at) FROM storefront.business_info WHERE \"key\" = @key",
            ("key", key),
            ("updated_at", updatedAt)))!;

    internal async Task<int> RowCountAsync() =>
        (int)(await ScalarAsync("SELECT count(*)::int FROM storefront.business_info"))!;

    internal async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync();
    }

    /// <summary>Runs one statement that PostgreSQL is expected to reject.</summary>
    internal Task<PostgresException> RejectedAsync(
        string sql,
        params (string Name, object? Value)[] parameters) =>
        Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, parameters));

    /// <summary>A scalar text result, where a stored NULL is a null value rather than DBNull.</summary>
    private static string? Text(object? value) => value is null or DBNull ? null : (string)value;

    private async Task<IReadOnlyList<string>> ColumnAsync(string sql)
    {
        var values = new List<string>();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }
}
