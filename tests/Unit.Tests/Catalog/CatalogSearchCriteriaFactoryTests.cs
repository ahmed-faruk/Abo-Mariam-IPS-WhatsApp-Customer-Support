using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// The normalization every search goes through before the PostgreSQL statement runs: the criteria
/// are what the SQL compares against, so they must be deterministic and bounded.
/// </summary>
public sealed class CatalogSearchCriteriaFactoryTests
{
    private const decimal SizeTolerance = 0.5m;
    private const decimal SoftTolerance = 0.15m;
    private const int MaxResults = 20;

    [Fact]
    public void Text_filters_are_normalized_to_the_comparison_form()
    {
        var criteria = Build(new ProductSearchQuery
        {
            Text = "  UltraSharp ",
            ModelCode = " p2419h ",
            Brand = "DELL",
            UseCase = "Programming ",
        });

        Assert.Equal("ultrasharp", criteria.Text);
        Assert.Equal("p2419h", criteria.ModelCode);
        Assert.Equal("dell", criteria.Brand);
        Assert.Equal("programming", criteria.UseCase);
    }

    [Fact]
    public void Missing_filters_stay_unset_instead_of_matching_nothing()
    {
        var criteria = Build(new ProductSearchQuery());

        Assert.Null(criteria.Text);
        Assert.Null(criteria.ModelCode);
        Assert.Null(criteria.Brand);
        Assert.Null(criteria.SizeInches);
        Assert.Null(criteria.PanelType);
        Assert.Null(criteria.MinResolutionWidth);
        Assert.Null(criteria.MinResolutionHeight);
        Assert.Null(criteria.MinRefreshRate);
        Assert.Null(criteria.BudgetMin);
        Assert.Null(criteria.BudgetMax);
        Assert.Null(criteria.UseCase);
        Assert.Empty(criteria.RequiredPorts);
        Assert.Empty(criteria.Grades);
        Assert.Equal(0, criteria.SizeToleranceInches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-24)]
    public void A_size_that_cannot_constrain_a_catalogue_is_dropped(decimal size)
    {
        var criteria = Build(new ProductSearchQuery { SizeInches = size });

        Assert.Null(criteria.SizeInches);
        Assert.Equal(0, criteria.SizeToleranceInches);
    }

    [Fact]
    public void A_stated_size_is_kept_with_the_configured_tolerance()
    {
        var criteria = Build(new ProductSearchQuery { SizeInches = 24m });

        Assert.Equal(24m, criteria.SizeInches);
        Assert.Equal(SizeTolerance, criteria.SizeToleranceInches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1080)]
    public void Minimums_that_are_not_above_zero_are_dropped(int value)
    {
        var criteria = Build(new ProductSearchQuery
        {
            MinResolutionWidth = value,
            MinResolutionHeight = value,
            MinRefreshRate = value,
        });

        Assert.Null(criteria.MinResolutionWidth);
        Assert.Null(criteria.MinResolutionHeight);
        Assert.Null(criteria.MinRefreshRate);
    }

    [Fact]
    public void Panel_and_grades_are_canonicalized_to_the_stored_spelling()
    {
        var criteria = Build(new ProductSearchQuery { PanelType = "ips", Grades = ["b", "A", "b"] });

        Assert.Equal("IPS", criteria.PanelType);
        Assert.Equal(["B", "A"], criteria.Grades);
    }

    [Fact]
    public void Required_ports_are_normalized_as_a_set()
    {
        var criteria = Build(new ProductSearchQuery { RequiredPorts = ["HDMI", " displayport ", "hdmi"] });

        Assert.Equal(["hdmi", "displayport"], criteria.RequiredPorts);
    }

    [Fact]
    public void A_hard_ceiling_reaches_the_criteria_unchanged()
    {
        var criteria = Build(new ProductSearchQuery { Budget = ProductBudget.Hard(2500m) });

        Assert.Null(criteria.BudgetMin);
        Assert.Equal(2500m, criteria.BudgetMax);
        Assert.Equal(2500m, criteria.BudgetTarget);
    }

    [Fact]
    public void A_soft_target_widens_the_criteria_by_the_configured_tolerance()
    {
        var criteria = Build(new ProductSearchQuery { Budget = ProductBudget.Soft(3000m) });

        Assert.Equal(3450m, criteria.BudgetMax);
        Assert.Equal(3000m, criteria.BudgetTarget);
    }

    [Fact]
    public void A_range_reaches_the_criteria_inclusively()
    {
        var criteria = Build(new ProductSearchQuery { Budget = ProductBudget.Range(2000m, 3000m) });

        Assert.Equal(2000m, criteria.BudgetMin);
        Assert.Equal(3000m, criteria.BudgetMax);
        Assert.Null(criteria.BudgetTarget);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(5, 5)]
    [InlineData(50, 20)]
    [InlineData(0, 20)]
    [InlineData(-3, 20)]
    public void The_requested_limit_is_always_bounded_by_the_configured_maximum(int? requested, int expected)
    {
        var criteria = Build(new ProductSearchQuery { Limit = requested });

        Assert.Equal(expected, criteria.Limit);
    }

    [Fact]
    public void A_negative_size_tolerance_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CatalogSearchCriteriaFactory.From(new ProductSearchQuery(), -0.5m, SoftTolerance, MaxResults));
    }

    [Fact]
    public void A_result_maximum_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CatalogSearchCriteriaFactory.From(new ProductSearchQuery(), SizeTolerance, SoftTolerance, 0));
    }

    [Fact]
    public void A_missing_query_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => CatalogSearchCriteriaFactory.From(null!, SizeTolerance, SoftTolerance, MaxResults));
    }

    private static CatalogSearchCriteria Build(ProductSearchQuery query) =>
        CatalogSearchCriteriaFactory.From(query, SizeTolerance, SoftTolerance, MaxResults);
}
