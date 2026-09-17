using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Identity.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Persistence;

/// <summary>
/// Exercises the real composition root: the tests never build DbContext options by hand, so a
/// broken module registration or a wrong migration history table fails the suite.
/// </summary>
internal static class ModulePersistence
{
    public static ServiceProvider BuildHost(string connectionString)
    {
        var configuration = new ConfigurationManager
        {
            [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(configuration);

        return services.BuildServiceProvider();
    }

    public static async Task MigrateAllAsync(string connectionString)
    {
        await using var provider = BuildHost(connectionString);
        await using var scope = provider.CreateAsyncScope();

        foreach (var context in Contexts(scope.ServiceProvider))
        {
            await context.Database.MigrateAsync();
        }
    }

    public static IEnumerable<DbContext> Contexts(IServiceProvider provider) =>
    [
        provider.GetRequiredService<CatalogDbContext>(),
        provider.GetRequiredService<ConversationDbContext>(),
        provider.GetRequiredService<MessagingDbContext>(),
        provider.GetRequiredService<StorefrontDbContext>(),
        provider.GetRequiredService<IdentityDbContext>(),
    ];

    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> AppliedMigrationsAsync(
        string connectionString)
    {
        await using var provider = BuildHost(connectionString);
        await using var scope = provider.CreateAsyncScope();

        var applied = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var context in Contexts(scope.ServiceProvider))
        {
            applied[context.GetType().Name] = [.. await context.Database.GetAppliedMigrationsAsync()];
        }

        return applied;
    }
}
