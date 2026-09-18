using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Identity.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Host.Web.Composition;

/// <summary>
/// Composition root for the modular monolith. Host.Web is the only place allowed to reference
/// module Infrastructure types, and it only calls the module-owned registration members, so no
/// Host.Web type ever depends on a module DbContext.
/// </summary>
public static class CompositionRoot
{
    /// <summary>The configuration key that carries the Npgsql connection string.</summary>
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddApplicationComposition(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. "
                + "Set it with dotnet user-secrets or the ConnectionStrings__DefaultConnection "
                + "environment variable; never commit credentials.");
        }

        services.AddCatalogModule(connectionString);
        services.AddConversationPersistence(connectionString);
        services.AddMessagingModule(connectionString);
        services.AddStorefrontPersistence(connectionString);
        services.AddIdentityPersistence(connectionString);

        return services;
    }
}
