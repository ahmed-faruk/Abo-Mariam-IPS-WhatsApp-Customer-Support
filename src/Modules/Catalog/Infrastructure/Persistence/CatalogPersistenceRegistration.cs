using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The only member of Catalog Infrastructure that the composition root calls. Keeping the
/// registration inside the module is what keeps Host.Web free of DbContext references.
/// </summary>
public static class CatalogPersistenceRegistration
{
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<CatalogDbContext>(options =>
            CatalogDbContext.Configure(options, connectionString));

        return services;
    }
}
