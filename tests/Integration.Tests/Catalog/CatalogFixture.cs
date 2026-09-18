using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Every catalogue test starts from a brand-new migrated PostgreSQL database, so no test can depend
/// on another test's durable state. The helpers below seed the catalogue of
/// docs/TECHNICAL.md section 6.1 directly, because Issue #6 covers search, details and the commercial
/// updates rather than product authoring.
/// </summary>
public abstract class CatalogFixture(PostgresContainerFixture postgres) : IAsyncLifetime
{
    internal string ConnectionString { get; private set; } = string.Empty;

    internal DatabaseCatalogReader Catalog { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = await postgres.CreateMigratedDatabaseAsync();
        Catalog = new DatabaseCatalogReader(ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    internal CatalogHost StartHost(Action<CatalogSearchOptions>? configure = null) =>
        CatalogHost.Start(ConnectionString, configure);

    internal static ICatalogSearch Search(IServiceProvider services) =>
        services.GetRequiredService<ICatalogSearch>();

    internal static ICatalogProductDetails Details(IServiceProvider services) =>
        services.GetRequiredService<ICatalogProductDetails>();

    internal static ICatalogCommercialUpdates Commercials(IServiceProvider services) =>
        services.GetRequiredService<ICatalogCommercialUpdates>();

    internal async Task<long> AddModelAsync(
        string modelCode,
        string brand = "Dell",
        string? model = null,
        string? displayName = null,
        decimal sizeInches = 24m,
        string panelType = "IPS",
        int resolutionWidth = 1920,
        int resolutionHeight = 1080,
        int refreshRate = 60,
        string[]? tags = null,
        bool isActive = true)
    {
        const string sql = """
            INSERT INTO catalog.product_model
                (model_code, brand, model, display_name, size_inches, panel_type,
                 resolution_width, resolution_height, refresh_rate, search_tags, is_active)
            VALUES
                (@model_code, @brand, @model, @display_name, @size_inches, @panel_type,
                 @resolution_width, @resolution_height, @refresh_rate, @search_tags, @is_active)
            RETURNING id;
            """;

        return (long)(await ScalarAsync(
            sql,
            ("model_code", modelCode),
            ("brand", brand),
            ("model", model ?? modelCode),
            ("display_name", displayName ?? $"{brand} {modelCode}"),
            ("size_inches", sizeInches),
            ("panel_type", panelType),
            ("resolution_width", resolutionWidth),
            ("resolution_height", resolutionHeight),
            ("refresh_rate", refreshRate),
            ("search_tags", tags ?? []),
            ("is_active", isActive)))!;
    }

    internal async Task<long> AddVariantAsync(
        long productModelId,
        string sku,
        string grade = "A",
        decimal price = 1000m,
        int quantity = 1,
        bool isActive = true,
        int warrantyDays = 30,
        string? warrantyNotes = null,
        string? cosmeticNotes = null)
    {
        const string sql = """
            INSERT INTO catalog.product_variant
                (product_model_id, sku, grade, selling_price, quantity, warranty_days,
                 warranty_notes, cosmetic_notes, is_active)
            VALUES
                (@product_model_id, @sku, @grade, @selling_price, @quantity, @warranty_days,
                 @warranty_notes, @cosmetic_notes, @is_active)
            RETURNING id;
            """;

        return (long)(await ScalarAsync(
            sql,
            ("product_model_id", productModelId),
            ("sku", sku),
            ("grade", grade),
            ("selling_price", price),
            ("quantity", quantity),
            ("warranty_days", warrantyDays),
            ("warranty_notes", warrantyNotes),
            ("cosmetic_notes", cosmeticNotes),
            ("is_active", isActive)))!;
    }

    internal async Task AddPortAsync(long productModelId, string portType, int count = 1)
    {
        const string sql = """
            INSERT INTO catalog.product_model_port (product_model_id, port_type, count)
            VALUES (@product_model_id, @port_type, @count);
            """;

        await ExecuteAsync(
            sql,
            ("product_model_id", productModelId),
            ("port_type", portType),
            ("count", count));
    }

    internal Task<string> StoredPriceAsync(long variantId) =>
        Catalog.ScalarAsync($"SELECT selling_price::text FROM catalog.product_variant WHERE id = {variantId}");

    internal Task<string> StoredQuantityAsync(long variantId) =>
        Catalog.ScalarAsync($"SELECT quantity::text FROM catalog.product_variant WHERE id = {variantId}");

    internal Task<string> StoredActiveStateAsync(long variantId) =>
        Catalog.ScalarAsync($"SELECT is_active::text FROM catalog.product_variant WHERE id = {variantId}");

    internal Task<string> StoredModelActiveStateAsync(long modelId) =>
        Catalog.ScalarAsync($"SELECT is_active::text FROM catalog.product_model WHERE id = {modelId}");

    internal Task<string> AuditCountAsync() => Catalog.ScalarAsync("SELECT count(*)::text FROM catalog.audit_log");

    internal async Task<IReadOnlyList<AuditRow>> AuditsAsync()
    {
        const string sql = """
            SELECT entity_type, entity_id, action, old_json::text, new_json::text, user_id, created_at::text
            FROM catalog.audit_log
            ORDER BY id;
            """;

        var rows = new List<AuditRow>();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                EntityType: reader.GetString(0),
                EntityId: reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Action: reader.GetString(2),
                OldJson: reader.IsDBNull(3) ? null : reader.GetString(3),
                NewJson: reader.IsDBNull(4) ? null : reader.GetString(4),
                UserId: reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt: reader.GetString(6)));
        }

        return rows;
    }

    /// <summary>Rejects the next audit insert, so a test can prove the change rolls back with it.</summary>
    internal Task RejectAuditInsertsAsync() =>
        Catalog.ExecuteAsync(
            """
            CREATE FUNCTION catalog.reject_audit_insert() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'audit insert refused by the test';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER test_reject_audit_insert
            BEFORE INSERT ON catalog.audit_log
            FOR EACH ROW EXECUTE FUNCTION catalog.reject_audit_insert();
            """);

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

    internal async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>One stored audit row, read back from the real PostgreSQL table.</summary>
internal sealed record AuditRow(
    string EntityType,
    long? EntityId,
    string Action,
    string? OldJson,
    string? NewJson,
    string? UserId,
    string CreatedAt);
