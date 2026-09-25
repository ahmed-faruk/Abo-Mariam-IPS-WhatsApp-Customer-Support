using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Registers the controlled-demo Messaging reset of docs/TECHNICAL.md section 36.5. Only the Host.Web
/// demo composition used by the operator tooling calls it, after
/// <see cref="MessagingModuleRegistration.AddMessagingModule"/>; the production composition root never
/// does.
/// </summary>
public static class MessagingDemoDataRegistration
{
    public static IServiceCollection AddMessagingDemoData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDemoMessagingData, DemoMessagingDataStore>();

        return services;
    }
}
