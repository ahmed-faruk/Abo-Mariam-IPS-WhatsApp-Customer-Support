using Npgsql;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Issue #6 acceptance: the canonical comparison of the module (trim, collapse whitespace runs, case
/// fold) is the one PostgreSQL uses on both sides. Stored values that only differ by case or
/// whitespace still match a normalized query, and two model codes that are logically the same code
/// cannot coexist, so an exact lookup can never be ambiguous.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogCanonicalTextTests(PostgresContainerFixture postgres) : CatalogFixture(postgres)
{
    private const string CanonicalCodeIndex = "uq_product_model_canonical_code";

    [Theory]
    [InlineData("p2419h")]
    [InlineData("  P2419H  ")]
    [InlineData("P2419H   ")]
    [InlineData("P2419H\t")]
    public async Task A_code_that_only_differs_by_case_or_surrounding_whitespace_is_rejected(string duplicate)
    {
        await AddModelAsync("P2419H");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => AddModelAsync(duplicate));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Contains(CanonicalCodeIndex, exception.Message, StringComparison.Ordinal);
        Assert.Equal("1", await Catalog.ScalarAsync("SELECT count(*)::text FROM catalog.product_model"));
    }

    [Fact]
    public async Task A_code_that_only_differs_by_a_whitespace_run_is_rejected()
    {
        await AddModelAsync("P 2419H");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => AddModelAsync("P\t  2419H"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Contains(CanonicalCodeIndex, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_canonical_uniqueness_does_not_merge_codes_that_are_actually_different()
    {
        await AddModelAsync("P2419H");

        // A space is part of the code, so collapsing the run keeps "P 2419H" a different code from
        // "P2419H", and a longer code is different as well.
        await AddModelAsync("P 2419H");
        await AddModelAsync("P2419HX");

        Assert.Equal("3", await Catalog.ScalarAsync("SELECT count(*)::text FROM catalog.product_model"));
    }

    [Fact]
    public async Task The_canonical_unique_index_is_an_expression_index_over_canonical_text()
    {
        var indexes = await Catalog.IndexesAsync();

        var index = indexes.Single(entry => entry.Name == CanonicalCodeIndex);

        Assert.True(index.IsUnique);
        Assert.False(index.IsPrimary);
        Assert.Equal("catalog.product_model", index.Table);
        Assert.Contains("canonical_text", index.Definition, StringComparison.Ordinal);
        Assert.Contains("model_code", index.Definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stored_code_brand_and_ports_that_only_differ_by_case_or_whitespace_still_match()
    {
        var model = await AddModelAsync(
            "  P2419H  ",
            brand: " Dell   Technologies ",
            model: "  P2419H Ultra  ",
            displayName: "  Dell P2419H Ultra  ");
        await AddVariantAsync(model, "SKU-CANONICAL", price: 4200m, quantity: 2);
        await AddPortAsync(model, " HDMI ");
        await AddPortAsync(model, "DisplayPort  ");

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        var lookup = await search.FindByModelCodeAsync("p2419h");

        Assert.NotNull(lookup);
        Assert.Equal(model, lookup.ModelId);

        var byBrand = await search.SearchAsync(new ProductSearchQuery { Brand = "  DELL technologies " });

        Assert.Equal(model, Assert.Single(byBrand).ModelId);

        var byPorts = await search.SearchAsync(new ProductSearchQuery { RequiredPorts = ["hdmi", "  DisplayPort "] });

        Assert.Equal(model, Assert.Single(byPorts).ModelId);
    }

    [Fact]
    public async Task A_space_is_part_of_the_code_so_each_lookup_finds_exactly_its_own_model()
    {
        var spaced = await AddModelAsync("P 2419H");
        var spacedVariant = await AddVariantAsync(spaced, "SKU-SPACED");
        var compact = await AddModelAsync("P2419H");
        var compactVariant = await AddVariantAsync(compact, "SKU-COMPACT");

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        // Collapsing the whitespace run does not remove the space, so the two codes stay distinct for
        // the lookup, exactly as the unique index keeps them distinct for insertion.
        var compactLookup = await search.FindByModelCodeAsync("  p2419h  ");
        var spacedLookup = await search.FindByModelCodeAsync(" p 2419h ");

        Assert.Equal(compact, compactLookup!.ModelId);
        Assert.Equal(compactVariant, compactLookup.VariantId);
        Assert.Equal(spaced, spacedLookup!.ModelId);
        Assert.Equal(spacedVariant, spacedLookup.VariantId);
    }

    [Fact]
    public async Task Required_ports_count_canonical_port_types_once()
    {
        var duplicated = await AddModelAsync("DUPLICATED-PORT");
        await AddVariantAsync(duplicated, "SKU-DUPLICATED-PORT");

        // Both rows are stored raw values PostgreSQL allows, but they are one canonical port type, so
        // asking for HDMI once must match and asking for HDMI plus DisplayPort must not.
        await AddPortAsync(duplicated, " HDMI ");
        await AddPortAsync(duplicated, "  HDMI  ");

        var other = await AddModelAsync("OTHER-PORT");
        await AddVariantAsync(other, "SKU-OTHER-PORT");
        await AddPortAsync(other, "hdmi");
        await AddPortAsync(other, " DISPLAYPORT ");

        await using var host = StartHost();
        await using var scope = host.CreateScope();
        var search = Search(scope.ServiceProvider);

        var repeated = await search.SearchAsync(
            new ProductSearchQuery { RequiredPorts = ["HDMI", "hdmi", " HDMI "] });

        Assert.Equal([duplicated, other], repeated.Select(item => item.ModelId));

        var bothPorts = await search.SearchAsync(
            new ProductSearchQuery { RequiredPorts = ["hdmi", "displayport"] });

        Assert.Equal(other, Assert.Single(bothPorts).ModelId);
    }
}
