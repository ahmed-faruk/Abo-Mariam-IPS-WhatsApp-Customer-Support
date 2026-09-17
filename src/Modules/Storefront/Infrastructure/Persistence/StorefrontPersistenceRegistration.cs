using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>The only member of Storefront Infrastructure that the composition root calls.</summary>
public static class StorefrontPersistenceRegistration
{
    public static IServiceCollection AddStorefrontPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<StorefrontDbContext>(options =>
            StorefrontDbContext.Configure(options, connectionString));

        return services;
    }
}
