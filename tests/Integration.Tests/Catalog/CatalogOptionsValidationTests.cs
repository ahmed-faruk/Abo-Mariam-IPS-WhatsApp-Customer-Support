using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Host.Web.Composition;
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
            (nameof(CatalogSearchOptions.MaxResults), options => options.MaxResults = 21),
            (nameof(CatalogSearchOptions.MaxResults), options => options.MaxResults = 1000),
            (nameof(CatalogSearchOptions.MaxResults), options => options.MaxResults = int.MaxValue),
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
        Assert.Equal(CatalogSearchPolicy.MaximumResults, options.MaxResults);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(CatalogSearchPolicy.MaximumResults)]
    public void A_maximum_within_the_search_policy_is_accepted(int maxResults)
    {
        using var provider = BuildProvider(options => options.MaxResults = maxResults);

        Assert.Equal(maxResults, ResolveSearchOptions(provider).MaxResults);
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

    [Fact]
    public async Task The_documented_configuration_keys_start_the_host_with_the_supplied_policy()
    {
        // Fully qualified: WhatsAppMonitorAssistant.Host.Web is a namespace in scope here.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        // Exactly the keys docs/CONFIGURATION.md and the checked-in appsettings.json template name.
        builder.Configuration["ConnectionStrings:DefaultConnection"] = UnusedConnectionString;
        builder.Configuration["Catalog:Search:SizeToleranceInches"] = "0.3";
        builder.Configuration["Catalog:Search:SoftBudgetTolerance"] = "0.45";
        ConfigureValidWhatsAppOptions(builder.Configuration);

        builder.Services.AddApplicationComposition(builder.Configuration);

        // The Messaging workers keep polling a queue; this test covers the composition root's
        // configuration contract, so only the real startup validation runs.
        builder.Services.RemoveAll<IHostedService>();

        using var host = builder.Build();

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<CatalogSearchOptions>>().Value;

        Assert.Equal(0.3m, options.RequiredSizeToleranceInches);
        Assert.Equal(0.45m, options.RequiredSoftBudgetTolerance);

        await host.StopAsync();
    }

    [Fact]
    public async Task A_composition_root_without_the_search_policy_fails_clearly_before_serving()
    {
        // Fully qualified: WhatsAppMonitorAssistant.Host.Web is a namespace in scope here.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:DefaultConnection"] = UnusedConnectionString;
        ConfigureValidWhatsAppOptions(builder.Configuration);
        builder.Services.AddApplicationComposition(builder.Configuration);
        builder.Services.RemoveAll<IHostedService>();

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(CatalogSearchOptions.SizeToleranceInches), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CatalogSearchOptions.SoftBudgetTolerance), exception.Message, StringComparison.Ordinal);
        Assert.Contains(CatalogSearchOptions.ConfigurationSectionName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documented_environment_variable_names_bind_the_required_settings()
    {
        // A private prefix keeps the process-wide environment untouched, and the double underscores are
        // the documented mapping from the Catalog__Search__... variable names to the configuration keys.
        const string prefix = "MONITOR_TEST_";
        const string sizeVariable = prefix + "Catalog__Search__SizeToleranceInches";
        const string softVariable = prefix + "Catalog__Search__SoftBudgetTolerance";

        Environment.SetEnvironmentVariable(sizeVariable, "0.3");
        Environment.SetEnvironmentVariable(softVariable, "0.45");

        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var section = configuration.GetSection(CatalogSearchOptions.ConfigurationSectionName);

            Assert.Equal("0.3", section[nameof(CatalogSearchOptions.SizeToleranceInches)]);
            Assert.Equal("0.45", section[nameof(CatalogSearchOptions.SoftBudgetTolerance)]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(sizeVariable, null);
            Environment.SetEnvironmentVariable(softVariable, null);
        }
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

    private static void ConfigureValidWhatsAppOptions(IConfiguration configuration)
    {
        configuration["WhatsApp:ApiVersion"] = "v23.0";
        configuration["WhatsApp:PhoneNumberId"] = "123456789";
        configuration["WhatsApp:VerifyToken"] = "verify-token";
        configuration["WhatsApp:AppSecret"] = "app-secret";
        configuration["WhatsApp:AccessToken"] = "access-token";
    }
}
