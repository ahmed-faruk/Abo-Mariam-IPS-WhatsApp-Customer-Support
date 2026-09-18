using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
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

        // The tolerated distances are explicit deployment configuration: the project baseline defines
        // a soft budget tolerance and a size tolerance but no values for them, so an unset one fails
        // startup validation naming the setting instead of silently applying an invented default. The
        // required keys, their environment variable names and the local workflow are documented in
        // docs/CONFIGURATION.md and named in the checked-in appsettings.json template.
        services.AddCatalogModule(connectionString, options =>
            configuration.GetSection(CatalogSearchOptions.ConfigurationSectionName).Bind(options));
        services.AddConversationPersistence(connectionString);
        services.AddMessagingModule(connectionString);
        services.AddStorefrontPersistence(connectionString);
        services.AddIdentityPersistence(connectionString);

        return services;
    }
}
