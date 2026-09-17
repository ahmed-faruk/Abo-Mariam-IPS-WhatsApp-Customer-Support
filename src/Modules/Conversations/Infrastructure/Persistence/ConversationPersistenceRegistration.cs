using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>The only member of Conversations Infrastructure that the composition root calls.</summary>
public static class ConversationPersistenceRegistration
{
    public static IServiceCollection AddConversationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<ConversationDbContext>(options =>
            ConversationDbContext.Configure(options, connectionString));

        return services;
    }
}
