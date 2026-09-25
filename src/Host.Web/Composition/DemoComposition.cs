using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

namespace WhatsAppMonitorAssistant.Host.Web.Composition;

/// <summary>
/// The demo-data composition of docs/TECHNICAL.md section 36.5. It adds the module-owned demo seed and
/// reset contracts on top of <see cref="CompositionRoot.AddApplicationComposition"/> for the demo
/// operator tooling only. Program.cs never calls it, so the running application cannot resolve a demo
/// mutation contract.
/// </summary>
public static class DemoComposition
{
    public static IServiceCollection AddDemoDataOperations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCatalogDemoData();
        services.AddStorefrontDemoData();
        services.AddConversationsDemoData();
        services.AddMessagingDemoData();

        return services;
    }
}
