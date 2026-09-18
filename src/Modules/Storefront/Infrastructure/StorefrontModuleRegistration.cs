using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;
using WhatsAppMonitorAssistant.Modules.Storefront.Features.UpdateBusinessInfo;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

/// <summary>
/// The only member of Storefront Infrastructure that the composition root calls. It wires the module's
/// persistence and the business-info use cases that later modules consume through Contracts.
/// </summary>
public static class StorefrontModuleRegistration
{
    public static IServiceCollection AddStorefrontModule(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddStorefrontPersistence(connectionString);

        services.AddScoped<IBusinessInfoReader, BusinessInfoReader>();
        services.AddScoped<IBusinessInfoUpdateStore, BusinessInfoUpdateStore>();

        services.AddScoped<IStorefrontBusinessInfo, GetBusinessInfoHandler>();
        services.AddScoped<IStorefrontBusinessInfoUpdates, UpdateBusinessInfoHandler>();

        return services;
    }
}
