using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Builds the real Messaging module registration over a throwaway PostgreSQL database, with the
/// queue policy left open so a test can prove the configured attempt limit is the one that applies.
/// </summary>
internal sealed class MessagingHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public static MessagingHost Start(
        string connectionString,
        Action<MessagingQueueOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessagingModule(connectionString, configure);
        configureServices?.Invoke(services);

        return new MessagingHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
