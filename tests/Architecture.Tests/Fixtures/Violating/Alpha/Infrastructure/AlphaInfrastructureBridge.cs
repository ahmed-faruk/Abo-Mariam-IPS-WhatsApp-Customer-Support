using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

/// <summary>Violates: a module must not reference another module's Infrastructure.</summary>
public sealed class AlphaInfrastructureBridge(BetaRepository repository)
{
    public int Count => repository.Count;
}
