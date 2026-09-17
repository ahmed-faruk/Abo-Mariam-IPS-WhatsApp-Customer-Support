namespace WhatsAppMonitorAssistant.Host.Web.Composition;

/// <summary>
/// Composition root for the modular monolith. Host.Web is the only place that is
/// allowed to reference module Infrastructure types; the module wiring extensions
/// are registered here as the feature tickets add them.
/// </summary>
public static class CompositionRoot
{
    public static IServiceCollection AddApplicationComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
