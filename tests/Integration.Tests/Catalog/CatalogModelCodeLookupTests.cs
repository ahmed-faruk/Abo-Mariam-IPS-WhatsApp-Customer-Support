using WhatsAppMonitorAssistant.Integration.Tests.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Issue #6 acceptance: an exact normalized model-code lookup has the highest precedence and never
/// returns a code that is not actually available.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogModelCodeLookupTests(PostgresContainerFixture postgres) : CatalogFixture(postgres)
{
    [Theory]
    [InlineData("P2419H")]
    [InlineData("p2419h")]
    [InlineData("  P2419H  ")]
    public async Task A_model_code_is_found_ignoring_case_and_surrounding_whitespace(string requested)
    {
        var model = await AddModelAsync("P2419H");
        var variant = await AddVariantAsync(model, "SKU-P2419H", price: 4200m, quantity: 2);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = await Search(scope.ServiceProvider).FindByModelCodeAsync(requested);

        Assert.NotNull(result);
        Assert.Equal(model, result.ModelId);
        Assert.Equal(variant, result.VariantId);
        Assert.Equal("P2419H", result.ModelCode);
    }

    [Fact]
    public async Task The_best_eligible_variant_represents_the_exact_code()
    {
        var model = await AddModelAsync("P2419H");
        await AddVariantAsync(model, "SKU-P2419H-A", grade: "A", price: 4500m, quantity: 0);
        var gradeB = await AddVariantAsync(model, "SKU-P2419H-B", grade: "B", price: 3900m);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        var result = await Search(scope.ServiceProvider).FindByModelCodeAsync("P2419H");

        Assert.NotNull(result);
        Assert.Equal(gradeB, result.VariantId);
        Assert.Equal(3900m, result.Price);
    }

    [Fact]
    public async Task An_unknown_code_returns_nothing()
    {
        var model = await AddModelAsync("P2419H");
        await AddVariantAsync(model, "SKU-P2419H");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await Search(scope.ServiceProvider).FindByModelCodeAsync("U2720Q"));
    }

    [Fact]
    public async Task A_code_whose_model_is_inactive_returns_nothing()
    {
        var model = await AddModelAsync("P2419H", isActive: false);
        await AddVariantAsync(model, "SKU-P2419H");

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await Search(scope.ServiceProvider).FindByModelCodeAsync("P2419H"));
    }

    [Fact]
    public async Task A_code_whose_stock_is_empty_returns_nothing()
    {
        var model = await AddModelAsync("P2419H");
        await AddVariantAsync(model, "SKU-P2419H", quantity: 0);

        await using var host = StartHost();
        await using var scope = host.CreateScope();

        Assert.Null(await Search(scope.ServiceProvider).FindByModelCodeAsync("P2419H"));
    }

    [Fact]
    public async Task A_blank_code_is_rejected_rather_than_looking_up_everything()
    {
        await using var host = StartHost();
        await using var scope = host.CreateScope();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Search(scope.ServiceProvider).FindByModelCodeAsync("   "));
    }
}
