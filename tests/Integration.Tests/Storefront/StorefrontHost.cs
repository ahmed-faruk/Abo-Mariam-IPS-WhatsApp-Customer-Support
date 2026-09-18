using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Builds the real Storefront module registration over a throwaway PostgreSQL database, so the tests
/// exercise the composition the host uses instead of a hand-built DbContext.
/// </summary>
internal sealed class StorefrontHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public static StorefrontHost Start(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorefrontModule(connectionString);

        return new StorefrontHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
