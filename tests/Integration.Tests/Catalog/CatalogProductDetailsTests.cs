using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Issue #6 acceptance: product details and the current-facts reload return the authoritative stored
/// values, so a caller always sees the current price, quantity and active state.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogProductDetailsTests(PostgresContainerFixture postgres) : CatalogFixture(postgres)
{
    [Fact]
    public async Task Details_return_the_model_specifications_ports_tags_and_every_variant()
    {
        var model = await AddModelAsync(
            "P2419H",
            brand: "Dell",
            model: "P2419H",
            displayName: "Dell P2419H 24\" IPS",
            sizeInches: 23.8m,
            panelType: "IPS",
            resolutionWidth: 1920,
            resolutionHeight: 1080,
            refreshRate: 60,
            tags: ["Office", "Programming"]);
        var gradeA = await AddVariantAsync(model, "SKU-P2419H-A", grade: "A", price: 4200m, quantity: 2, warrantyDays: 90);
        var gradeC = await AddVariantAsync(model, "SKU-P2419H-C", grade: "C", price: 3100m, quantity: 1);
        await AddVariantAsync(model, "SKU-P2419H-B", grade: "B", price: 3800m, quantity: 0);
        await AddPortAsync(model, "HDMI", count: 1);
        await AddPortAsync(model, "DisplayPort", count: 2);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var details = await Details(scope.ServiceProvider).GetDetailsAsync(model);

        Assert.NotNull(details);
        Assert.Equal("P2419H", details.ModelCode);
        Assert.Equal("Dell", details.Brand);
        Assert.Equal(23.8m, details.SizeInches);
        Assert.Equal("IPS", details.PanelType);
        Assert.Equal(1920, details.ResolutionWidth);
        Assert.Equal(1080, details.ResolutionHeight);
        Assert.Equal(60, details.RefreshRate);
        Assert.True(details.IsActive);
        Assert.Equal(["Office", "Programming"], details.Tags);
        Assert.Equal([new ProductPortFact("DisplayPort", 2), new ProductPortFact("HDMI", 1)], details.Ports);

        var available = details.Variants.Where(variant => variant.IsAvailable).Select(variant => variant.VariantId);

        Assert.Equal([gradeA, gradeC], available);

        var gradeARow = Assert.Single(details.Variants, variant => variant.VariantId == gradeA);

        Assert.Equal(4200m, gradeARow.Price);
        Assert.Equal(2, gradeARow.Quantity);
        Assert.Equal(90, gradeARow.WarrantyDays);
        Assert.True(gradeARow.IsAvailable);

        var gradeCRow = Assert.Single(details.Variants, variant => variant.VariantId == gradeC);

        Assert.True(gradeCRow.IsAvailable);

        var outOfStock = Assert.Single(details.Variants, variant => variant.Grade == "B");

        Assert.False(outOfStock.IsAvailable);
    }

    [Fact]
    public async Task An_inactive_model_marks_every_variant_of_it_unavailable()
    {
        var model = await AddModelAsync("P2419H", isActive: false);
        var variant = await AddVariantAsync(model, "SKU-P2419H", quantity: 4);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var details = await Details(scope.ServiceProvider).GetDetailsAsync(model);

        Assert.NotNull(details);
        Assert.False(details.IsActive);
        Assert.False(Assert.Single(details.Variants, row => row.VariantId == variant).IsAvailable);
    }

    [Fact]
    public async Task An_inactive_variant_stays_visible_in_the_details_but_never_as_available()
    {
        var model = await AddModelAsync("P2419H");
        var variant = await AddVariantAsync(model, "SKU-P2419H", isActive: false, quantity: 3);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var row = Assert.Single((await Details(scope.ServiceProvider).GetDetailsAsync(model))!.Variants);

        Assert.Equal(variant, row.VariantId);
        Assert.False(row.IsActive);
        Assert.False(row.IsAvailable);
    }

    [Fact]
    public async Task An_unknown_model_has_no_details()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await Details(scope.ServiceProvider).GetDetailsAsync(4242));
    }

    [Fact]
    public async Task A_non_positive_model_id_is_rejected()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Details(scope.ServiceProvider).GetDetailsAsync(0));
    }

    [Fact]
    public async Task The_current_facts_reload_returns_the_live_commercial_values()
    {
        var model = await AddModelAsync("P2419H", displayName: "Dell P2419H 24\" IPS");
        var variant = await AddVariantAsync(model, "SKU-P2419H", price: 4200m, quantity: 2, warrantyDays: 90);
        await AddPortAsync(model, "HDMI");
        await AddPortAsync(model, "VGA");

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var details = Details(scope.ServiceProvider);

        var facts = await details.GetVariantFactsAsync(variant);

        Assert.NotNull(facts);
        Assert.Equal(model, facts.ModelId);
        Assert.Equal("Dell P2419H 24\" IPS", facts.DisplayName);
        Assert.Equal(4200m, facts.Price);
        Assert.Equal(2, facts.Quantity);
        Assert.Equal(90, facts.WarrantyDays);
        Assert.Equal(["HDMI", "VGA"], facts.Ports);
        Assert.True(facts.IsAvailable);

        await Commercials(scope.ServiceProvider).UpdatePriceAsync(new VariantPriceUpdate(variant, 3999m, "admin-1"));
        await Commercials(scope.ServiceProvider).UpdateQuantityAsync(new VariantQuantityUpdate(variant, 0, "admin-1"));

        var reloaded = await details.GetVariantFactsAsync(variant);

        Assert.NotNull(reloaded);
        Assert.Equal(3999m, reloaded.Price);
        Assert.Equal(0, reloaded.Quantity);
        Assert.False(reloaded.IsAvailable);
    }

    [Fact]
    public async Task An_unknown_variant_has_no_current_facts()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await Details(scope.ServiceProvider).GetVariantFactsAsync(4242));
    }

    [Fact]
    public async Task A_deactivated_variant_stops_being_recommended_and_stops_being_available()
    {
        var model = await AddModelAsync("P2419H");
        var variant = await AddVariantAsync(model, "SKU-P2419H");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.NotNull(await Search(scope.ServiceProvider).FindByModelCodeAsync("P2419H"));

        await Commercials(scope.ServiceProvider).UpdateActiveStateAsync(
            new VariantActiveStateUpdate(variant, IsActive: false, ActorUserId: "admin-1"));

        Assert.Null(await Search(scope.ServiceProvider).FindByModelCodeAsync("P2419H"));
        Assert.False((await Details(scope.ServiceProvider).GetVariantFactsAsync(variant))!.IsAvailable);
    }
}
