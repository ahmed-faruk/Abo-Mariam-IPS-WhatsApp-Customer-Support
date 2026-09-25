using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// Registers the controlled-demo conversation reset of docs/TECHNICAL.md section 36.5. Only the
/// Host.Web demo composition used by the operator tooling calls it, after
/// <see cref="ConversationsModuleRegistration.AddConversationsModule"/>; the production composition
/// root never does.
/// </summary>
public static class ConversationsDemoDataRegistration
{
    public static IServiceCollection AddConversationsDemoData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDemoConversationData, DemoConversationDataStore>();

        return services;
    }
}
