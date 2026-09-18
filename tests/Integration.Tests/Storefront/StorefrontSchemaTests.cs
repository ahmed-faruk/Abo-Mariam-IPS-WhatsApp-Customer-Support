using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Issue #7 acceptance: the Storefront baseline schema of docs/TECHNICAL.md section 6.2 is the one the
/// module reads and writes, and PostgreSQL itself enforces its constraints.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StorefrontSchemaTests(PostgresContainerFixture postgres) : StorefrontFixture(postgres)
{
    [Fact]
    public async Task The_baseline_table_and_its_documented_columns_exist()
    {
        var columns = await BusinessInfoColumnsAsync();

        Assert.Equal(["id", "key", "answer_ar", "answer_en", "is_active", "updated_at"], columns);
    }

    [Fact]
    public async Task A_duplicate_key_is_rejected()
    {
        await SeedAsync(BusinessInfoKeys.Address, "شارع 1");

        var exception = await RejectedAsync(
            """INSERT INTO storefront.business_info ("key", answer_ar) VALUES (@key, @answer_ar);""",
            ("key", BusinessInfoKeys.Address),
            ("answer_ar", "شارع 2"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_missing_arabic_answer_is_rejected()
    {
        var exception = await RejectedAsync(
            """INSERT INTO storefront.business_info ("key", answer_ar) VALUES (@key, @answer_ar);""",
            ("key", BusinessInfoKeys.Delivery),
            ("answer_ar", null));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
    }

    [Fact]
    public async Task PostgreSQL_supplies_the_documented_defaults()
    {
        var id = await SeedAsync(BusinessInfoKeys.Delivery, "توصيل لكل المحافظات");

        Assert.True(id > 0);
        Assert.Equal("true", await StoredActiveStateAsync(BusinessInfoKeys.Delivery));
        Assert.NotNull(await StoredUpdatedAtAsync(BusinessInfoKeys.Delivery));
    }
}
