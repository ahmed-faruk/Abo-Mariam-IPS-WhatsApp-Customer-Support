using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The only member of Conversations Infrastructure that the composition root calls. It wires the
/// module's persistence, its orchestration and its contracts.
/// </summary>
public static class ConversationsModuleRegistration
{
    /// <summary>
    /// Registers the Conversations module. Until the deterministic renderer of Issue #12 is bound, the
    /// module is registered with <see cref="FailClosedConversationRenderer"/>, so the host starts
    /// normally and no customer-facing text can be produced accidentally.
    /// </summary>
    public static IServiceCollection AddConversationsModule(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddConversationPersistence(connectionString);
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IConversationTurnStore, ConversationTurnStore>();
        services.AddScoped<ConversationIntentRouter>();
        services.AddScoped<IProcessInboundTurn, ProcessInboundTurnHandler>();
        services.AddScoped<IConversationModeControl, ConversationModeControl>();
        services.AddScoped<IInboundMessageProcessor, MessagingInboundMessageProcessor>();

        services.TryAddSingleton<IConversationRenderer, FailClosedConversationRenderer>();

        return services;
    }
}
