using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Features;

/// <summary>Violates: a module must not reference another module's Infrastructure.</summary>
public sealed class BetaFeatureHandler(AlphaRepository repository)
{
    public int Count() => repository.Count;
}
