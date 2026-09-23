using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Meta;
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
        Action<MessagingQueueOptions>? configure = null,
        Action<WhatsAppOptions>? configureWhatsApp = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMessagingPersistence(connectionString);
        services.AddMessagingQueues(configure);
        services.AddWhatsAppTransport(configureWhatsApp);

        return services;
    }

    private static void AddWhatsAppTransport(this IServiceCollection services, Action<WhatsAppOptions>? configure)
    {
        if (configure is null)
        {
            return;
        }

        var configured = new WhatsAppOptions();
        configure.Invoke(configured);

        var whatsAppOptions = services.AddOptions<WhatsAppOptions>()
            .Configure(options => Copy(configured, options));

        whatsAppOptions.Services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();
        whatsAppOptions.ValidateOnStart();

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value);
        services.AddHttpClient<IOutboundMessageSender, MetaOutboundMessageSender>((provider, client) =>
        {
            var options = provider.GetRequiredService<WhatsAppOptions>();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(WhatsAppRateLimit.PolicyName, limiter =>
            {
                limiter.PermitLimit = configured.WebhookPermitLimit;
                limiter.Window = TimeSpan.FromSeconds(configured.WebhookWindowSeconds);
                limiter.QueueLimit = 0;
            });
        });
    }

    private static void Copy(WhatsAppOptions source, WhatsAppOptions target)
    {
        target.ApiVersion = source.ApiVersion;
        target.PhoneNumberId = source.PhoneNumberId;
        target.WabaId = source.WabaId;
        target.VerifyToken = source.VerifyToken;
        target.AppSecret = source.AppSecret;
        target.AccessToken = source.AccessToken;
        target.TimeoutSeconds = source.TimeoutSeconds;
        target.MaxWebhookBodyBytes = source.MaxWebhookBodyBytes;
        target.WebhookPermitLimit = source.WebhookPermitLimit;
        target.WebhookWindowSeconds = source.WebhookWindowSeconds;
    }

    private static void AddMessagingQueues(this IServiceCollection services, Action<MessagingQueueOptions>? configure)
    {
        var queueOptions = services.AddOptions<MessagingQueueOptions>()
            .Configure(options => configure?.Invoke(options));

        queueOptions.Services.AddSingleton<IValidateOptions<MessagingQueueOptions>, MessagingQueueOptionsValidator>();

        // An invalid policy is rejected at startup, so no worker can ever poll in a failure loop.
        queueOptions.ValidateOnStart();

        // The workers and the stores read one validated instance of the queue policy.
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<MessagingQueueOptions>>().Value);

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
