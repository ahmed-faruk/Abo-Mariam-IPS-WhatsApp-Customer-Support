using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

/// <summary>
/// Registers the controlled-demo business-info seed of docs/TECHNICAL.md section 36.5. Only the
/// Host.Web demo composition used by the operator tooling calls it, after
/// <see cref="StorefrontModuleRegistration.AddStorefrontModule"/>; the production composition root
/// never does.
/// </summary>
public static class StorefrontDemoDataRegistration
{
    public static IServiceCollection AddStorefrontDemoData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDemoBusinessInfoData, DemoBusinessInfoDataStore>();

        return services;
    }
}
