using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

/// <summary>
/// Registers the controlled-demo catalogue seed of docs/TECHNICAL.md section 36.5. Only the Host.Web
/// demo composition used by the operator tooling calls it, after
/// <see cref="CatalogModuleRegistration.AddCatalogModule"/>; the production composition root never does.
/// </summary>
public static class CatalogDemoDataRegistration
{
    public static IServiceCollection AddCatalogDemoData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDemoCatalogData, DemoCatalogDataStore>();

        return services;
    }
}
