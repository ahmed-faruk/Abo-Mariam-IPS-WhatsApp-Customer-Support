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
