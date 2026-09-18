using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Catalog;

/// <summary>
/// Builds the real Catalog module registration over a throwaway PostgreSQL database, with the search
/// policy left open so a test can prove the configured tolerance and bound are the ones that apply.
/// </summary>
internal sealed class CatalogHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public static CatalogHost Start(
        string connectionString,
        Action<CatalogSearchOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogModule(connectionString, configure);

        return new CatalogHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
