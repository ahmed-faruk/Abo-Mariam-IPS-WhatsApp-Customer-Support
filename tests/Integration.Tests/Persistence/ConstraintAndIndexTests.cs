using Npgsql;

namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>
/// Issue #4 acceptance: the documented constraints and indexes exist and PostgreSQL enforces them.
/// Expected values come from docs/TECHNICAL.md section 6.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConstraintAndIndexTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private static readonly string[] ExpectedCheckConstraints =
    [
        "ck_conversation_mode",
        "ck_inbox_status",
        "ck_model_panel",
        "ck_model_refresh",
        "ck_model_resolution",
        "ck_model_size",
        "ck_outbox_body_hash",
        "ck_outbox_sender",
        "ck_outbox_status",
        "ck_port_count",
        "ck_variant_grade",
        "ck_variant_price",
        "ck_variant_quantity",
        "ck_variant_warranty",
    ];

    private static readonly string[] ExpectedPartialIndexes =
    [
        "ix_inbox_claim",
        "ix_outbox_claim",
        "ux_conversation_open_customer",
    ];

    private static readonly string[] ExpectedNamedUniqueIndexes =
    [
        "uq_model_port",
        "uq_variant_model_grade",
        "ux_outbox_correlation_id",
        "ux_conversation_open_customer",
    ];

    private DatabaseCatalogReader catalog = null!;
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Every_documented_check_constraint_exists()
    {
        var names = await catalog.CheckConstraintNamesAsync();

        Assert.All(ExpectedCheckConstraints, name => Assert.Contains(name, names));
    }

    [Fact]
    public async Task Check_constraints_carry_the_documented_predicates()
    {
        var definitions = await catalog.CheckConstraintDefinitionsAsync();

        Assert.Contains(definitions, definition => definition.Contains("ck_outbox_body_hash", StringComparison.Ordinal)
            && definition.Contains("body_hash = sha256", StringComparison.Ordinal)
            && definition.Contains("convert_to(body", StringComparison.Ordinal));
        Assert.Contains(definitions, definition => definition.Contains("ck_model_panel", StringComparison.Ordinal)
            && definition.Contains("'IPS'", StringComparison.Ordinal));
        Assert.Contains(definitions, definition => definition.Contains("ck_conversation_mode", StringComparison.Ordinal)
            && definition.Contains("'Human'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Documented_unique_and_partial_indexes_exist()
    {
        var indexes = await catalog.IndexesAsync();

        Assert.All(ExpectedNamedUniqueIndexes, name =>
            Assert.Contains(indexes, index => index.Name == name && index.IsUnique && !index.IsPrimary));

        Assert.All(ExpectedPartialIndexes, name =>
            Assert.Contains(indexes, index => index.Name == name && index.Predicate.Length > 0));

        Assert.Contains(indexes, index => index.Name == "ix_inbox_claim"
            && index.Predicate.Contains("processing_status = 'Pending'", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Name == "ix_outbox_claim"
            && index.Predicate.Contains("delivery_status = 'Pending'", StringComparison.Ordinal));
        Assert.Contains(indexes, index => index.Name == "ux_conversation_open_customer"
            && index.Predicate.Contains("mode <> 'Closed'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("catalog.product_model", "model_code")]
    [InlineData("catalog.product_variant", "sku")]
    [InlineData("conversations.customer", "whatsapp_number")]
    [InlineData("storefront.business_info", "key")]
    [InlineData("messaging.webhook_envelope", "envelope_hash")]
    [InlineData("messaging.inbox_message", "provider_message_id")]
    [InlineData("messaging.outbox_message", "provider_message_id")]
    public async Task Documented_columns_are_unique(string table, string column)
    {
        var indexes = await catalog.IndexesAsync();

        Assert.Contains(indexes, index => index.Table == table
            && index.IsUnique
            && index.Definition.Contains($"({column})", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostgreSQL_rejects_a_grade_outside_the_documented_values()
    {
        await InsertModelAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => catalog.ExecuteAsync(
            "INSERT INTO catalog.product_variant (product_model_id, sku, grade, selling_price) "
            + "VALUES (1, 'SKU-D', 'D', 100)"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("ck_variant_grade", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSQL_rejects_a_negative_price()
    {
        await InsertModelAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => catalog.ExecuteAsync(
            "INSERT INTO catalog.product_variant (product_model_id, sku, grade, selling_price) "
            + "VALUES (1, 'SKU-NEG', 'A', -1)"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("ck_variant_price", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSQL_rejects_an_outbox_body_hash_that_does_not_match_the_body()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, body, body_hash, partition_key) "
            + "VALUES (1, '20100000000', 'corr-1', 'hello', sha256('other'::bytea), 'p-1')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("ck_outbox_body_hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSQL_accepts_an_outbox_body_hash_that_matches_the_body()
    {
        await catalog.ExecuteAsync(
            "INSERT INTO messaging.outbox_message "
            + "(conversation_id, customer_external_id, correlation_id, body, body_hash, partition_key) "
            + "VALUES (1, '20100000000', 'corr-2', 'hello', sha256('hello'::bytea), 'p-1')");

        var status = await catalog.ScalarAsync(
            "SELECT delivery_status FROM messaging.outbox_message WHERE correlation_id = 'corr-2'");

        Assert.Equal("Pending", status);
    }

    [Fact]
    public async Task Only_one_open_conversation_per_customer_is_allowed()
    {
        await catalog.ExecuteAsync("INSERT INTO conversations.customer (whatsapp_number) VALUES ('20100000001')");
        await catalog.ExecuteAsync("INSERT INTO conversations.conversation (customer_id, mode) VALUES (1, 'AI')");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => catalog.ExecuteAsync(
            "INSERT INTO conversations.conversation (customer_id, mode) VALUES (1, 'Human')"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Contains("ux_conversation_open_customer", exception.Message, StringComparison.Ordinal);

        await catalog.ExecuteAsync("INSERT INTO conversations.conversation (customer_id, mode) VALUES (1, 'Closed')");

        var conversationCount = await catalog.ScalarAsync("SELECT count(*) FROM conversations.conversation");

        // The rejected second open conversation is not stored; the AI and Closed rows remain.
        Assert.Equal("2", conversationCount);
    }

    private Task InsertModelAsync() => catalog.ExecuteAsync(
        "INSERT INTO catalog.product_model "
        + "(model_code, brand, model, display_name, size_inches, panel_type, resolution_width, resolution_height, refresh_rate) "
        + "VALUES ('P2419H', 'Dell', 'P2419H', 'Dell P2419H 24\" IPS', 24.0, 'IPS', 1920, 1080, 60)");
}
