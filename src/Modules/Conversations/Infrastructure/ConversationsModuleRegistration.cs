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
    /// Registers the Conversations module, including the deterministic renderer of Issue #12 that writes
    /// every customer-facing reply from the current Catalog and Storefront facts.
    /// <see cref="FailClosedConversationRenderer"/> stays available as the explicit fail-closed seam a
    /// test binds when it wants the behaviour of a host that has no renderer at all.
    /// </summary>
    public static IServiceCollection AddConversationsModule(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddConversationPersistence(connectionString);
        services.TryAddSingleton(TimeProvider.System);

        // One coordinator per scope, shared by the turn store and the mode-control seam, so both
        // serialize on the same PostgreSQL conversation lock.
        services.AddScoped<ConversationOperationCoordinator>();
        services.AddScoped<IConversationTurnStore, ConversationTurnStore>();
        services.AddScoped<ConversationIntentRouter>();
        services.AddScoped<IProcessInboundTurn, ProcessInboundTurnHandler>();
        services.AddScoped<IConversationModeControl, ConversationModeControl>();
        services.AddScoped<IInboundMessageProcessor, MessagingInboundMessageProcessor>();

        // The deterministic renderer reads the current facts through the Catalog and Storefront contracts,
        // which are scoped to their module's DbContext, so the renderer is scoped to the same scope as the
        // turn that uses it. A singleton here would capture those readers.
        services.TryAddScoped<IConversationRenderer, DeterministicConversationRenderer>();

        return services;
    }
}
