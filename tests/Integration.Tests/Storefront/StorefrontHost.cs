using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Storefront;

/// <summary>
/// Builds the real Storefront module registration over a throwaway PostgreSQL database, so the tests
/// exercise the composition the host uses instead of a hand-built DbContext.
/// </summary>
internal sealed class StorefrontHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public static StorefrontHost Start(string connectionString, string? applicationName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStorefrontModule(WithApplicationName(connectionString, applicationName));

        return new StorefrontHost(services.BuildServiceProvider());
    }

    /// <summary>
    /// Names the module's sessions when a test asks for it, so a concurrency test can recognize the
    /// module's own connection while it waits on a row lock.
    /// </summary>
    private static string WithApplicationName(string connectionString, string? applicationName) =>
        applicationName is null
            ? connectionString
            : new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = applicationName }
                .ConnectionString;

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
