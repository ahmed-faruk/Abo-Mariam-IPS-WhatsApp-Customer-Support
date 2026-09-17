using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>The only member of Messaging Infrastructure that the composition root calls.</summary>
public static class MessagingPersistenceRegistration
{
    public static IServiceCollection AddMessagingPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<MessagingDbContext>(options =>
            MessagingDbContext.Configure(options, connectionString));

        return services;
    }
}
