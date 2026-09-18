using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.GetProductDetails;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.UpdateVariantCommercials;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

/// <summary>
/// The only member of Catalog Infrastructure that the composition root calls. It wires the module's
/// persistence, its search policy and the use cases that later modules consume through Contracts.
/// </summary>
public static class CatalogModuleRegistration
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        string connectionString,
        Action<CatalogSearchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCatalogPersistence(connectionString);

        var searchOptions = services.AddOptions<CatalogSearchOptions>()
            .Configure(options => configure?.Invoke(options));

        searchOptions.Services.AddSingleton<IValidateOptions<CatalogSearchOptions>, CatalogSearchOptionsValidator>();

        // An invalid search policy is rejected at startup, so no query can silently run with an
        // unbounded result set or a meaningless tolerance.
        searchOptions.ValidateOnStart();

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<CatalogSearchOptions>>().Value);

        services.AddScoped<IProductSearchReader, ProductSearchReader>();
        services.AddScoped<IProductDetailsReader, ProductDetailsReader>();
        services.AddScoped<IVariantCommercialUpdateStore, VariantCommercialUpdateStore>();

        services.AddScoped<ICatalogSearch, SearchProductsHandler>();
        services.AddScoped<ICatalogProductDetails, GetProductDetailsHandler>();
        services.AddScoped<ICatalogCommercialUpdates, VariantCommercialUpdatesHandler>();

        return services;
    }
}
