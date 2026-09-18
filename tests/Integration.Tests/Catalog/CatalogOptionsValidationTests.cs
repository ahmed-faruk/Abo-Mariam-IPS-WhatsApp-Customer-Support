using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// An invalid search policy is rejected before a query can run with it, instead of silently
/// searching with a meaningless tolerance or an unbounded result set.
/// </summary>
public sealed class CatalogOptionsValidationTests
{
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=5432;Database=catalog_options;Username=monitor_app";

    [Fact]
    public void Every_invalid_search_setting_is_rejected_and_named()
    {
        (string Setting, Action<CatalogSearchOptions> Configure)[] invalid =
        [
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = -0.1m),
            (nameof(CatalogSearchOptions.SizeToleranceInches), options => options.SizeToleranceInches = -1m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = -0.01m),
            (nameof(CatalogSearchOptions.SoftBudgetTolerance), options => options.SoftBudgetTolerance = -2m),
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
    public void The_documented_default_policy_is_accepted()
    {
        using var provider = BuildProvider(_ => { });

        var options = ResolveSearchOptions(provider);

        Assert.True(options.SizeToleranceInches >= 0);
        Assert.True(options.SoftBudgetTolerance >= 0);
        Assert.True(options.MaxResults > 0);
    }

    [Fact]
    public async Task A_host_with_an_invalid_search_setting_fails_before_serving()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddCatalogModule(
            UnusedConnectionString,
            options => options.MaxResults = 0);

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(CatalogSearchOptions.MaxResults), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Action<CatalogSearchOptions> configure) =>
        new ServiceCollection()
            .AddLogging()
            .AddCatalogModule(UnusedConnectionString, configure)
            .BuildServiceProvider();

    private static CatalogSearchOptions ResolveSearchOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<CatalogSearchOptions>>().Value;
}
