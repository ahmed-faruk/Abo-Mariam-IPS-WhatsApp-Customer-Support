using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// The Admin Lite catalogue: the read-only grid contract on the seeded demo data, and the page that
/// shows it on the admin listener.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed partial class AdminLiteCatalogTests(PostgresContainerFixture postgres)
{
    private const string Page = "/admin/catalog";
    private const string KeySku = "DEMO-P2419H-A";

    [Fact]
    public async Task G1_the_grid_lists_every_seeded_variant_in_model_code_then_grade_order()
    {
        await using var factory = await StartSeededAsync();

        var rows = await GridAsync(factory);
        var expected = DemoDataset.Catalogue
            .SelectMany(model => model.Variants.Select(variant => (model.ModelCode, model.DisplayName, variant)))
            .OrderBy(entry => entry.ModelCode.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(entry => entry.variant.Grade, StringComparer.Ordinal)
            .Select(entry => (entry.ModelCode, entry.DisplayName, entry.variant.Sku, entry.variant.Grade, entry.variant.SellingPrice, entry.variant.Quantity, true))
            .ToList();

        Assert.Equal(expected, rows.Select(row => (row.ModelCode, row.DisplayName, row.Sku, row.Grade, row.Price, row.Quantity, row.IsActive)));

        using var client = factory.AdminClient();
        var html = await client.GetStringAsync(Page);

        Assert.Equal(expected.Select(entry => entry.Sku), SkuRows().Matches(html).Select(match => match.Groups["sku"].Value));
    }

    [Fact]
    public async Task G2_out_of_stock_and_inactive_variants_stay_in_the_grid()
    {
        await using var factory = await StartSeededAsync();
        var keyId = (await GridAsync(factory)).Single(row => row.Sku == KeySku).VariantId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var updates = scope.ServiceProvider.GetRequiredService<ICatalogCommercialUpdates>();
            await updates.UpdateQuantityAsync(new VariantQuantityUpdate(keyId, 0, "test-operator"));
            await updates.UpdateActiveStateAsync(new VariantActiveStateUpdate(keyId, false, "test-operator"));
        }

        var rows = await GridAsync(factory);
        var key = Assert.Single(rows, row => row.Sku == KeySku);

        Assert.Equal(25, rows.Count);
        Assert.Equal((0, false), (key.Quantity, key.IsActive));
    }

    [Fact]
    public async Task P1_a_price_edit_is_audited_under_the_demo_actor_and_seen_by_the_customer_read_path()
    {
        await using var factory = await StartSeededAsync();
        var keyId = await KeyVariantIdAsync(factory);
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, "Price", Fields(keyId, "price", "2350"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal((2350m, true), await CustomerFactsAsync(factory, keyId));
        Assert.Equal("UpdatePrice|demo-operator", await LastAuditAsync(factory, keyId));
    }

    [Fact]
    public async Task P2_a_quantity_of_zero_makes_the_variant_unavailable_to_customers()
    {
        await using var factory = await StartSeededAsync();
        var keyId = await KeyVariantIdAsync(factory);
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, "Quantity", Fields(keyId, "quantity", "0"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal((2400m, false), await CustomerFactsAsync(factory, keyId));
        Assert.Equal("UpdateQuantity|demo-operator", await LastAuditAsync(factory, keyId));
    }

    [Theory]
    [InlineData("Price", "price", "-1")]
    [InlineData("Price", "price", "abc")]
    [InlineData("Price", "price", "")]
    [InlineData("Quantity", "quantity", "-1")]
    [InlineData("Quantity", "quantity", "1.5")]
    [InlineData("Quantity", "quantity", "abc")]
    public async Task P3_an_invalid_value_is_rejected_and_nothing_changes(string handler, string field, string value)
    {
        await using var factory = await StartSeededAsync();
        var keyId = await KeyVariantIdAsync(factory);
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, handler, Fields(keyId, field, value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((2400m, true), await CustomerFactsAsync(factory, keyId));
        Assert.Equal(string.Empty, await LastAuditAsync(factory, keyId));
    }

    [Fact]
    public async Task P4_a_post_without_an_antiforgery_token_is_rejected()
    {
        await using var factory = await StartSeededAsync();
        var keyId = await KeyVariantIdAsync(factory);
        using var client = factory.AdminClient();

        using var response = await client.PostAsync($"{Page}?handler=Price", new FormUrlEncodedContent(Fields(keyId, "price", "1")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((2400m, true), await CustomerFactsAsync(factory, keyId));
    }

    [Fact]
    public async Task P5_an_unknown_variant_is_not_found()
    {
        await using var factory = await StartSeededAsync();
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, "Price", Fields(987654321, "price", "100"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static KeyValuePair<string, string>[] Fields(long variantId, string field, string value) =>
        [new("variantId", variantId.ToString(System.Globalization.CultureInfo.InvariantCulture)), new(field, value)];

    private static async Task<long> KeyVariantIdAsync(AdminLiteHostFactory factory) =>
        (await GridAsync(factory)).Single(row => row.Sku == KeySku).VariantId;

    /// <summary>The price and availability the customer-facing renderer reads for a variant.</summary>
    private static async Task<(decimal Price, bool IsAvailable)> CustomerFactsAsync(AdminLiteHostFactory factory, long variantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var facts = await scope.ServiceProvider.GetRequiredService<ICatalogProductDetails>().GetVariantFactsAsync(variantId);

        return (facts!.Price, facts.IsAvailable);
    }

    private static Task<string> LastAuditAsync(AdminLiteHostFactory factory, long variantId) =>
        new DatabaseCatalogReader(factory.ConnectionString).ScalarAsync(
            "SELECT action || '|' || user_id FROM catalog.audit_log "
            + $"WHERE entity_id = {variantId} ORDER BY id DESC LIMIT 1");

    private async Task<AdminLiteHostFactory> StartSeededAsync()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        Assert.Equal(DemoCli.Success, await AdminForms.RunDemoOpsAsync(connectionString, "seed"));

        var factory = new AdminLiteHostFactory(connectionString);
        factory.StartServer();

        return factory;
    }

    private static async Task<IReadOnlyList<CatalogGridRow>> GridAsync(AdminLiteHostFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ICatalogAdminGrid>().ListVariantsAsync();
    }

    [GeneratedRegex("""<tr data-sku="(?<sku>[^"]+)">""", RegexOptions.CultureInvariant)]
    private static partial Regex SkuRows();
}
