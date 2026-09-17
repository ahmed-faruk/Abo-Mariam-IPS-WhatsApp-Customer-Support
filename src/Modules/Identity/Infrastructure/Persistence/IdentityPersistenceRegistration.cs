using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppMonitorAssistant.Modules.Identity.Infrastructure.Persistence;

/// <summary>The only member of Identity Infrastructure that the composition root calls.</summary>
public static class IdentityPersistenceRegistration
{
    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<IdentityDbContext>(options =>
            IdentityDbContext.Configure(options, connectionString));

        return services;
    }
}
