using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// The only member of Messaging Infrastructure that the composition root calls. It wires the
/// module's persistence, its durable queues and its workers.
/// </summary>
public static class MessagingModuleRegistration
{
    public static IServiceCollection AddMessagingModule(
        this IServiceCollection services,
        string connectionString,
        Action<MessagingQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMessagingPersistence(connectionString);
        services.AddMessagingQueues(configure);

        return services;
    }

    private static void AddMessagingQueues(this IServiceCollection services, Action<MessagingQueueOptions>? configure)
    {
        var options = new MessagingQueueOptions();
        configure?.Invoke(options);

        if (options.IdlePollDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.IdlePollDelay,
                "The idle poll delay must be positive so a worker with no work never busy-spins.");
        }

        services.AddSingleton(options);
        services.AddScoped<IInboundMessageQueue, InboundMessageQueue>();
        services.AddScoped<IInboxMessageStore, InboxMessageStore>();
        services.AddScoped<IOutboundMessageQueue, OutboundMessageQueue>();
        services.AddScoped<IOutboxMessageStore, OutboxMessageStore>();

        // The workers stay idle until a processor or a sender is registered by a later ticket.
        // Each one is a single instance, resolvable for tests and health checks, and started by the host.
        services.AddSingleton<InboxWorker>();
        services.AddSingleton<OutboxWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<InboxWorker>());
        services.AddHostedService(provider => provider.GetRequiredService<OutboxWorker>());
    }
}
