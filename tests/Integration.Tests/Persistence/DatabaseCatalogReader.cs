using Npgsql;

namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>Reads the live PostgreSQL system catalogue so assertions describe the real database.</summary>
internal sealed class DatabaseCatalogReader(string connectionString)
{
    private const string ExcludedSchemas = "'pg_catalog', 'information_schema'";

    public Task<IReadOnlyList<string>> SchemasAsync() =>
        ColumnAsync($"SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ({ExcludedSchemas})");

    public Task<IReadOnlyList<string>> TablesAsync() =>
        ColumnAsync(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables "
            + $"WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ({ExcludedSchemas})");

    public Task<IReadOnlyList<string>> CheckConstraintNamesAsync() =>
        ColumnAsync(
            "SELECT tc.constraint_name FROM information_schema.table_constraints tc "
            + $"WHERE tc.constraint_type = 'CHECK' AND tc.table_schema NOT IN ({ExcludedSchemas})");

    public Task<IReadOnlyList<string>> CheckConstraintDefinitionsAsync() =>
        ColumnAsync(
            "SELECT tc.constraint_name || ' | ' || cc.check_clause "
            + "FROM information_schema.table_constraints tc "
            + "JOIN information_schema.check_constraints cc "
            + "ON cc.constraint_schema = tc.constraint_schema AND cc.constraint_name = tc.constraint_name "
            + $"WHERE tc.constraint_type = 'CHECK' AND tc.table_schema NOT IN ({ExcludedSchemas})");

    public async Task<IReadOnlyList<IndexInfo>> IndexesAsync()
    {
        var rows = await RowsAsync(
            "SELECT n.nspname || '.' || t.relname, i.relname, x.indisunique::text, x.indisprimary::text, "
            + "coalesce(pg_get_expr(x.indpred, x.indrelid), ''), pg_get_indexdef(x.indexrelid) "
            + "FROM pg_index x "
            + "JOIN pg_class t ON t.oid = x.indrelid "
            + "JOIN pg_class i ON i.oid = x.indexrelid "
            + "JOIN pg_namespace n ON n.oid = t.relnamespace "
            + $"WHERE n.nspname NOT IN ({ExcludedSchemas})");

        return
        [
            .. rows.Select(row => new IndexInfo(
                Table: row[0],
                Name: row[1],
                IsUnique: row[2] == "true",
                IsPrimary: row[3] == "true",
                Predicate: row[4],
                Definition: row[5])),
        ];
    }

    public async Task<IReadOnlyList<ForeignKeyInfo>> ForeignKeysAsync()
    {
        var rows = await RowsAsync(
            "SELECT n.nspname, c.relname, rn.nspname, rc.relname, con.conname "
            + "FROM pg_constraint con "
            + "JOIN pg_class c ON c.oid = con.conrelid "
            + "JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "JOIN pg_class rc ON rc.oid = con.confrelid "
            + "JOIN pg_namespace rn ON rn.oid = rc.relnamespace "
            + $"WHERE con.contype = 'f' AND n.nspname NOT IN ({ExcludedSchemas})");

        return
        [
            .. rows.Select(row => new ForeignKeyInfo(
                ChildSchema: row[0],
                ChildTable: row[1],
                ParentSchema: row[2],
                ParentTable: row[3],
                Name: row[4])),
        ];
    }

    public Task<IReadOnlyList<string>> MigrationHistoryTablesAsync() =>
        ColumnAsync(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables "
            + "WHERE table_name = '__ef_migrations' "
            + $"AND table_type = 'BASE TABLE' AND table_schema NOT IN ({ExcludedSchemas})");

    public async Task<IReadOnlyList<PrimaryKeyColumnInfo>> PrimaryKeyColumnsAsync()
    {
        var rows = await RowsAsync(
            "SELECT tc.table_schema || '.' || tc.table_name, kcu.column_name "
            + "FROM information_schema.table_constraints tc "
            + "JOIN information_schema.key_column_usage kcu "
            + "ON kcu.constraint_schema = tc.constraint_schema AND kcu.constraint_name = tc.constraint_name "
            + $"WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema NOT IN ({ExcludedSchemas})");

        return [.. rows.Select(row => new PrimaryKeyColumnInfo(Table: row[0], Column: row[1]))];
    }

    public Task<IReadOnlyList<string>> MigrationHistoryEntriesAsync(string schema) =>
        ColumnAsync($"SELECT \"MigrationId\" FROM {schema}.__ef_migrations ORDER BY \"MigrationId\"");

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? string.Empty : value.ToString() ?? string.Empty;
    }

    private async Task<IReadOnlyList<string>> ColumnAsync(string sql)
    {
        var rows = await RowsAsync(sql);

        return [.. rows.Select(row => row[0])];
    }

    private async Task<List<string[]>> RowsAsync(string sql)
    {
        var rows = new List<string[]>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];

            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                values[ordinal] = reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal).ToString() ?? string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }
}

internal sealed record IndexInfo(
    string Table,
    string Name,
    bool IsUnique,
    bool IsPrimary,
    string Predicate,
    string Definition);

internal sealed record ForeignKeyInfo(
    string ChildSchema,
    string ChildTable,
    string ParentSchema,
    string ParentTable,
    string Name);

internal sealed record PrimaryKeyColumnInfo(string Table, string Column);
