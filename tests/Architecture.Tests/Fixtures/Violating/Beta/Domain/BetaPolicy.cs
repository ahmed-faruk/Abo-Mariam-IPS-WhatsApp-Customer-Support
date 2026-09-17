using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Host;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Domain;

/// <summary>Violates: a module must not depend on the Host.Web composition root.</summary>
public sealed class BetaPolicy(HostComposition composition)
{
    public string HostName => composition.Name;
}
