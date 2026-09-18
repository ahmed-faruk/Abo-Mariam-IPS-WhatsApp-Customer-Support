using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// The search policy has no tolerance defaults, because docs/PLAN.md and docs/TECHNICAL.md define a
/// configurable soft budget tolerance and a size tolerance without defining their values. A missing or
/// out-of-range setting is therefore rejected at startup instead of being replaced by an invented one.
/// </summary>
public sealed class CatalogOptionsValidationTests
{
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=5432;Database=catalog_options;Username=monitor_app";

    [Fact]
    public void A_policy_without_its_tolerances_is_rejected_and_names_the_missing_settings()
    {
        using var provider = BuildProvider(_ => { }, configureTolerances: false);

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveSearchOptions(provider));

        Assert.Contains(nameof(CatalogSearchOptions.SizeToleranceInches), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CatalogSearchOptions.SoftBudgetTolerance), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(CatalogSearchOptions.SizeToleranceInches))]
    [InlineData(nameof(CatalogSearchOptions.SoftBudgetTolerance))]
    public void Missing_each_tolerance_on_its_own_is_rejected_and_named(string missingSetting)
    {
        using var provider = BuildProvider(options =>
        {
            if (missingSetting == nameof(CatalogSearchOptions.SizeToleranceInches))
            {
                options.SizeToleranceInches = null;
            }
            else
            {
                options.SoftBudgetTolerance = null;
            }
        });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveSearchOptions(provider));

        Assert.Contains(missingSetting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_out_of_range_search_setting_is_rejected_and_named()
    {
        (string Setting, Action<CatalogSearchOptions> Configure)[] invalid =
        [
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = -0.1m),
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = -1m),
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = 50.1m),
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = 500m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = -0.01m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = -2m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = 1m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = 12m),
            (nameof(CatalogSearchOptions.MaxResults), options => options.MaxResults = 0),
            (nameof(CatalogSearchOptions.MaxResults), options => options.MaxResults = -5),
        ];

        foreach (var (setting, configure) in invalid)
        {
            using var provider = BuildProvider(configure);

            var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveSearchOptions(provider));

            Assert.True(
                exception.Message.Contains(setting, StringComparison.Ordinal),
                $"The rejected setting '{setting}' was not named in: {exception.Message}");
        }
    }

    [Fact]
    public void An_explicit_valid_policy_is_accepted()
    {
        using var provider = BuildProvider(_ => { });

        var options = ResolveSearchOptions(provider);

        Assert.Equal(CatalogFixture.TestSizeToleranceInches, options.RequiredSizeToleranceInches);
        Assert.Equal(CatalogFixture.TestSoftBudgetTolerance, options.RequiredSoftBudgetTolerance);
        Assert.True(options.MaxResults > 0);
    }

    [Fact]
    public void The_safe_bounds_of_each_tolerance_are_accepted()
    {
        using var provider = BuildProvider(options =>
        {
            options.SizeToleranceInches = 0m;
            options.SoftBudgetTolerance = 0m;
        });

        var options = ResolveSearchOptions(provider);

        Assert.Equal(0m, options.RequiredSizeToleranceInches);
        Assert.Equal(0m, options.RequiredSoftBudgetTolerance);

        using var widest = BuildProvider(options =>
        {
            options.SizeToleranceInches = ModelSizeBounds.LargestDifference;
            options.SoftBudgetTolerance = 0.999m;
        });

        Assert.Equal(ModelSizeBounds.LargestDifference, ResolveSearchOptions(widest).RequiredSizeToleranceInches);
    }

    [Fact]
    public async Task A_host_with_an_out_of_range_search_setting_fails_before_serving()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddCatalogModule(
            UnusedConnectionString,
            options =>
            {
                options.SizeToleranceInches = CatalogFixture.TestSizeToleranceInches;
                options.SoftBudgetTolerance = CatalogFixture.TestSoftBudgetTolerance;
                options.MaxResults = 0;
            });

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(CatalogSearchOptions.MaxResults), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_without_the_tolerances_fails_before_serving()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddCatalogModule(UnusedConnectionString);

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(CatalogSearchOptions.SizeToleranceInches), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CatalogSearchOptions.SoftBudgetTolerance), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(
        Action<CatalogSearchOptions> configure,
        bool configureTolerances = true) =>
        new ServiceCollection()
            .AddLogging()
            .AddCatalogModule(UnusedConnectionString, options =>
            {
                if (configureTolerances)
                {
                    options.SizeToleranceInches = CatalogFixture.TestSizeToleranceInches;
                    options.SoftBudgetTolerance = CatalogFixture.TestSoftBudgetTolerance;
                }

                configure(options);
            })
            .BuildServiceProvider();

    private static CatalogSearchOptions ResolveSearchOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<CatalogSearchOptions>>().Value;
}
