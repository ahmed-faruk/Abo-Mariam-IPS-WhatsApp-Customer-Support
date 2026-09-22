using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// Builds the real Conversations orchestration over a throwaway PostgreSQL database, together with the
/// real Messaging module, so the durable Outbox of a processed turn is the module's own row. Test
/// doubles are registered before the module registration, so they replace the renderer, the clock and
/// the AI and catalogue seams while the persistence stays real.
/// </summary>
internal sealed class ConversationsHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public static ConversationsHost Start(
        string connectionString,
        Action<IServiceCollection>? configureServices = null,
        Action<IServiceCollection>? overrideServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configureServices?.Invoke(services);

        services.AddMessagingModule(connectionString);
        services.AddConversationsModule(connectionString);

        // A test that has to replace a registration the modules own, such as the durable Outbox itself,
        // does it here: the last registration of a service wins.
        overrideServices?.Invoke(services);

        return new ConversationsHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
