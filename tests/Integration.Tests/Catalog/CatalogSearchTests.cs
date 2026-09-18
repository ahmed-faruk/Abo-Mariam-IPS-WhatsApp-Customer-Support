using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Issue #6 acceptance: the structured and lexical search runs in real PostgreSQL, honours every hard
/// filter, never recommends an unavailable variant, never exceeds a hard budget ceiling, returns at
/// most one recommendation per model, and orders the result deterministically.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogSearchTests(PostgresContainerFixture postgres) : CatalogFixture(postgres)
{
    [Fact]
    public async Task An_exact_model_code_returns_only_that_model_with_its_current_facts()
    {
        var p2419h = await AddModelAsync("P2419H", displayName: "Dell P2419H 24\" IPS");
        var variant = await AddVariantAsync(p2419h, "SKU-P2419H-A", price: 4200m, quantity: 3);
        await AddPortAsync(p2419h, "HDMI");
        await AddModelAsync("U2720Q", displayName: "Dell U2720Q 27\" IPS");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { ModelCode = "p2419h" }));

        Assert.Equal("P2419H", result.ModelCode);
        Assert.Equal(p2419h, result.ModelId);
        Assert.Equal(variant, result.VariantId);
        Assert.Equal(4200m, result.Price);
        Assert.Equal(3, result.Quantity);
        Assert.Equal("A", result.Grade);
        Assert.Equal(["HDMI"], result.Ports);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task Brand_size_panel_resolution_and_refresh_are_hard_filters()
    {
        var matching = await AddModelAsync(
            "MATCH",
            brand: "Dell",
            sizeInches: 23.8m,
            panelType: "IPS",
            resolutionWidth: 2560,
            resolutionHeight: 1440,
            refreshRate: 75);
        await AddVariantAsync(matching, "SKU-MATCH");

        var wrongBrand = await AddModelAsync("BRAND", brand: "Samsung");
        await AddVariantAsync(wrongBrand, "SKU-BRAND");

        var wrongSize = await AddModelAsync("SIZE", sizeInches: 27m);
        await AddVariantAsync(wrongSize, "SKU-SIZE");

        var wrongPanel = await AddModelAsync("PANEL", panelType: "TN");
        await AddVariantAsync(wrongPanel, "SKU-PANEL");

        var lowResolution = await AddModelAsync("RESOLUTION", resolutionWidth: 1366, resolutionHeight: 768);
        await AddVariantAsync(lowResolution, "SKU-RESOLUTION");

        var lowRefresh = await AddModelAsync("REFRESH", refreshRate: 60);
        await AddVariantAsync(lowRefresh, "SKU-REFRESH");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery
        {
            Brand = " dell ",
            SizeInches = 24m,
            PanelType = "ips",
            MinResolutionWidth = 1920,
            MinResolutionHeight = 1080,
            MinRefreshRate = 75,
        }));

        Assert.Equal(matching, result.ModelId);
    }

    [Fact]
    public async Task The_size_tolerance_is_configured_in_inches()
    {
        var twentyThreePointEight = await AddModelAsync("TOLERATED", sizeInches: 23.8m);
        await AddVariantAsync(twentyThreePointEight, "SKU-TOLERATED");

        var twentySix = await AddModelAsync("TOO-SMALL", sizeInches: 22m);
        await AddVariantAsync(twentySix, "SKU-TOO-SMALL");

        await using var host = StartHost(options => options.SizeToleranceInches = 0.25m);
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { SizeInches = 24m }));

        Assert.Equal(twentyThreePointEight, result.ModelId);
    }

    [Fact]
    public async Task Changing_the_configured_size_tolerance_changes_the_result_deterministically()
    {
        var twentyThreePointEight = await AddModelAsync("TOLERATED", sizeInches: 23.8m);
        await AddVariantAsync(twentyThreePointEight, "SKU-TOLERATED");

        await using var strict = StartHost(options => options.SizeToleranceInches = 0.1m);
        await using var permissive = StartHost(options => options.SizeToleranceInches = 0.25m);

        await using (var scope = strict.CreateScope())
        {
            // 0.1 inches does not reach a 23.8 inch panel asked for as 24.
            Assert.Empty(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery { SizeInches = 24m }));
        }

        await using (var scope = permissive.CreateScope())
        {
            Assert.Equal(
                twentyThreePointEight,
                Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
                    new ProductSearchQuery { SizeInches = 24m })).ModelId);
        }
    }

    [Fact]
    public async Task Required_ports_are_a_set_that_must_be_satisfied_completely()
    {
        var bothPorts = await AddModelAsync("BOTH-PORTS");
        await AddVariantAsync(bothPorts, "SKU-BOTH-PORTS");
        await AddPortAsync(bothPorts, "HDMI");
        await AddPortAsync(bothPorts, "DisplayPort");

        var hdmiOnly = await AddModelAsync("HDMI-ONLY");
        await AddVariantAsync(hdmiOnly, "SKU-HDMI-ONLY");
        await AddPortAsync(hdmiOnly, "HDMI");

        var noPorts = await AddModelAsync("NO-PORTS");
        await AddVariantAsync(noPorts, "SKU-NO-PORTS");

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        var single = await search.SearchAsync(new ProductSearchQuery { RequiredPorts = ["hdmi"] });

        Assert.Equal([bothPorts, hdmiOnly], single.Select(item => item.ModelId));

        var both = await search.SearchAsync(new ProductSearchQuery { RequiredPorts = ["HDMI", "displayport"] });

        Assert.Equal(bothPorts, Assert.Single(both).ModelId);
        Assert.Equal(["DisplayPort", "HDMI"], Assert.Single(both).Ports);

        var repeated = await search.SearchAsync(new ProductSearchQuery { RequiredPorts = ["HDMI", "hdmi", " HDMI "] });

        Assert.Equal([bothPorts, hdmiOnly], repeated.Select(item => item.ModelId));
    }

    [Fact]
    public async Task An_inactive_model_an_inactive_variant_and_an_empty_quantity_are_never_recommended()
    {
        var inactiveModel = await AddModelAsync("INACTIVE-MODEL", isActive: false);
        await AddVariantAsync(inactiveModel, "SKU-INACTIVE-MODEL");

        var inactiveVariant = await AddModelAsync("INACTIVE-VARIANT");
        await AddVariantAsync(inactiveVariant, "SKU-INACTIVE-VARIANT", isActive: false);

        var emptyStock = await AddModelAsync("EMPTY-STOCK");
        await AddVariantAsync(emptyStock, "SKU-EMPTY-STOCK", quantity: 0);

        var availableModel = await AddModelAsync("AVAILABLE");
        var availableVariant = await AddVariantAsync(availableModel, "SKU-AVAILABLE", quantity: 1);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery()));

        Assert.Equal(availableModel, result.ModelId);
        Assert.Equal(availableVariant, result.VariantId);
    }

    [Fact]
    public async Task Only_the_eligible_variant_of_a_partly_available_model_represents_it()
    {
        var model = await AddModelAsync("PARTLY");
        await AddVariantAsync(model, "SKU-PARTLY-A", grade: "A", price: 5000m, quantity: 0);
        var sellable = await AddVariantAsync(model, "SKU-PARTLY-B", grade: "B", price: 4000m, quantity: 2);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery()));

        Assert.Equal(sellable, result.VariantId);
        Assert.Equal("B", result.Grade);
    }

    [Fact]
    public async Task A_model_with_several_eligible_variants_contributes_exactly_one_recommendation()
    {
        var model = await AddModelAsync("MULTI");
        var gradeA = await AddVariantAsync(model, "SKU-MULTI-A", grade: "A", price: 5000m);
        await AddVariantAsync(model, "SKU-MULTI-B", grade: "B", price: 3000m);
        await AddVariantAsync(model, "SKU-MULTI-C", grade: "C", price: 2000m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery()));

        // Grade priority is the documented ranking, and the model never appears twice.
        Assert.Equal(gradeA, result.VariantId);
        Assert.Equal("A", result.Grade);
    }

    [Fact]
    public async Task A_grade_filter_restricts_the_variants_that_can_represent_a_model()
    {
        var model = await AddModelAsync("GRADED");
        await AddVariantAsync(model, "SKU-GRADED-A", grade: "A", price: 5000m);
        var gradeB = await AddVariantAsync(model, "SKU-GRADED-B", grade: "B", price: 4000m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var onlyB = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Grades = ["b"] }));

        Assert.Equal(gradeB, onlyB.VariantId);
        Assert.Equal("B", onlyB.Grade);

        var accepted = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Grades = ["B", "A"] }));

        Assert.Equal("A", accepted.Grade);
    }

    [Fact]
    public async Task A_hard_budget_ceiling_is_never_exceeded_and_the_ceiling_itself_is_allowed()
    {
        var atCeiling = await AddModelAsync("AT-CEILING");
        await AddVariantAsync(atCeiling, "SKU-AT-CEILING", price: 2500m);

        var aboveCeiling = await AddModelAsync("ABOVE-CEILING");
        await AddVariantAsync(aboveCeiling, "SKU-ABOVE-CEILING", price: 2500.01m);

        // A soft tolerance is configured as wide as it can be, and it still cannot widen the ceiling.
        await using var host = StartHost(options => options.SoftBudgetTolerance = 0.9m);
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Budget = ProductBudget.Hard(2500m) }));

        Assert.Equal(atCeiling, result.ModelId);
        Assert.True(result.Price <= 2500m);
    }

    [Fact]
    public async Task A_soft_budget_widens_the_search_by_the_configured_tolerance()
    {
        var inside = await AddModelAsync("INSIDE-TOKEN");
        await AddVariantAsync(inside, "SKU-INSIDE-TOKEN", price: 3500m);

        var outside = await AddModelAsync("OUTSIDE-TOKEN");
        await AddVariantAsync(outside, "SKU-OUTSIDE-TOKEN", price: 3700m);

        await using var host = StartHost(options => options.SoftBudgetTolerance = 0.2m);
        await using var scope = host.CreateScope();

        var result = Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Budget = ProductBudget.Soft(3000m) }));

        // The configured 0.2 widens the stated 3000 to exactly 3600, so the 3500 model is eligible
        // while the 3700 model is not.
        Assert.Equal(inside, result.ModelId);
    }

    [Fact]
    public async Task Changing_the_configured_soft_tolerance_changes_eligibility_deterministically()
    {
        var aboveTheStrictCeiling = await AddModelAsync("BETWEEN-TOLERANCES");
        await AddVariantAsync(aboveTheStrictCeiling, "SKU-BETWEEN-TOLERANCES", price: 3200m);

        await using var strict = StartHost(options => options.SoftBudgetTolerance = 0.05m);
        await using var permissive = StartHost(options => options.SoftBudgetTolerance = 0.1m);

        await using (var scope = strict.CreateScope())
        {
            // A 0.05 tolerance widens 3000 to 3150, which excludes a 3200 model.
            Assert.Empty(await Search(scope.ServiceProvider).SearchAsync(
                new ProductSearchQuery { Budget = ProductBudget.Soft(3000m) }));
        }

        await using (var scope = permissive.CreateScope())
        {
            // A 0.1 tolerance widens 3000 to 3300, which includes the same model.
            Assert.Equal(
                aboveTheStrictCeiling,
                Assert.Single(await Search(scope.ServiceProvider).SearchAsync(
                    new ProductSearchQuery { Budget = ProductBudget.Soft(3000m) })).ModelId);
        }
    }

    [Fact]
    public async Task A_range_budget_is_inclusive_at_both_ends()
    {
        var low = await AddModelAsync("LOW");
        await AddVariantAsync(low, "SKU-LOW", price: 2000m);

        var high = await AddModelAsync("HIGH");
        await AddVariantAsync(high, "SKU-HIGH", price: 3000m);

        var below = await AddModelAsync("BELOW");
        await AddVariantAsync(below, "SKU-BELOW", price: 1999.99m);

        var above = await AddModelAsync("ABOVE");
        await AddVariantAsync(above, "SKU-ABOVE", price: 3000.01m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Budget = ProductBudget.Range(2000m, 3000m) });

        // A range has no target to rank by, so the two inclusive ends are ordered by their model code.
        Assert.Equal([high, low], result.Select(item => item.ModelId));
    }

    [Fact]
    public async Task The_documented_ranking_orders_grade_then_budget_closeness_then_tag_then_lexical()
    {
        var gradeC = await AddModelAsync("ZZZ-C", sizeInches: 24m);
        await AddVariantAsync(gradeC, "SKU-ZZZ-C", grade: "C", price: 3000m);

        var gradeB = await AddModelAsync("YYY-B", sizeInches: 24m);
        await AddVariantAsync(gradeB, "SKU-YYY-B", grade: "B", price: 3000m);

        var gradeA = await AddModelAsync("XXX-A", sizeInches: 24m);
        await AddVariantAsync(gradeA, "SKU-XXX-A", grade: "A", price: 3000m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var ranked = await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery { SizeInches = 24m });

        Assert.Equal([gradeA, gradeB, gradeC], ranked.Select(item => item.ModelId));
    }

    [Fact]
    public async Task Among_equally_graded_models_the_closest_price_to_the_stated_target_comes_first()
    {
        var far = await AddModelAsync("FAR");
        await AddVariantAsync(far, "SKU-FAR", price: 2800m);

        var close = await AddModelAsync("CLOSE");
        await AddVariantAsync(close, "SKU-CLOSE", price: 3000m);

        var closest = await AddModelAsync("CLOSEST");
        await AddVariantAsync(closest, "SKU-CLOSEST", price: 2900m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var ranked = await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Budget = ProductBudget.Soft(2925m) });

        Assert.Equal([closest, close, far], ranked.Select(item => item.ModelId));
    }

    [Fact]
    public async Task A_curated_use_case_tag_ranks_its_models_first()
    {
        var tagged = await AddModelAsync("TAGGED", tags: ["Programming", "Office"]);
        await AddVariantAsync(tagged, "SKU-TAGGED", price: 5000m);

        var untagged = await AddModelAsync("AAA-UNTAGGED");
        await AddVariantAsync(untagged, "SKU-UNTAGGED", price: 1000m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var ranked = await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { UseCase = " programming " });

        Assert.Equal(tagged, ranked[0].ModelId);
        Assert.Equal([tagged, untagged], ranked.Select(item => item.ModelId));
    }

    [Fact]
    public async Task A_lexical_term_ranks_matching_names_first()
    {
        var matching = await AddModelAsync("AAA", displayName: "Dell UltraSharp 24");
        await AddVariantAsync(matching, "SKU-LEXICAL");

        var other = await AddModelAsync("ZZZ", displayName: "Samsung Odyssey 24");
        await AddVariantAsync(other, "SKU-OTHER");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var ranked = await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Text = " UltraSharp " });

        Assert.Equal(matching, ranked[0].ModelId);
        Assert.Equal([matching, other], ranked.Select(item => item.ModelId));
    }

    [Fact]
    public async Task Equal_ranked_models_are_tie_broken_by_the_stable_model_code_and_id()
    {
        var second = await AddModelAsync("MODEL-B");
        await AddVariantAsync(second, "SKU-MODEL-B", price: 2000m);

        var first = await AddModelAsync("model-a");
        await AddVariantAsync(first, "SKU-MODEL-A", price: 2000m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var ranked = await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery());

        Assert.Equal([first, second], ranked.Select(item => item.ModelId));
    }

    [Fact]
    public async Task The_result_set_is_bounded_by_the_configured_maximum()
    {
        for (var index = 1; index <= 4; index++)
        {
            var model = await AddModelAsync($"BOUND-{index}");
            await AddVariantAsync(model, $"SKU-BOUND-{index}");
        }

        await using var host = StartHost(options => options.MaxResults = 2);
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        Assert.Equal(2, (await search.SearchAsync(new ProductSearchQuery())).Count);
        Assert.Equal(2, (await search.SearchAsync(new ProductSearchQuery { Limit = 50 })).Count);
        Assert.Single(await search.SearchAsync(new ProductSearchQuery { Limit = 1 }));
    }

    [Fact]
    public async Task No_query_can_obtain_more_than_the_twenty_recommendations_of_the_search_policy()
    {
        for (var index = 1; index <= CatalogSearchPolicy.MaximumResults + 2; index++)
        {
            var model = await AddModelAsync($"POLICY-{index:D2}");
            await AddVariantAsync(model, $"SKU-POLICY-{index:D2}");
        }

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        Assert.Equal(
            CatalogSearchPolicy.MaximumResults,
            (await search.SearchAsync(new ProductSearchQuery { Limit = 1000 })).Count);
        Assert.Equal(
            CatalogSearchPolicy.MaximumResults,
            (await search.SearchAsync(new ProductSearchQuery())).Count);
    }

    [Fact]
    public async Task A_query_that_matches_nothing_returns_an_empty_result()
    {
        var model = await AddModelAsync("PRESENT");
        await AddVariantAsync(model, "SKU-PRESENT");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Empty(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery { Brand = "HP" }));
        Assert.Empty(await Search(scope.ServiceProvider).SearchAsync(new ProductSearchQuery { Grades = ["C"] }));
        Assert.Empty(await Search(scope.ServiceProvider).SearchAsync(
            new ProductSearchQuery { Budget = ProductBudget.Hard(100m) }));
    }
}
