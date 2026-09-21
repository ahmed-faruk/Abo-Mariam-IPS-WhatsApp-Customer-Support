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
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configureServices?.Invoke(services);

        services.AddMessagingModule(connectionString);
        services.AddConversationsModule(connectionString);

        return new ConversationsHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
