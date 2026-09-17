using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Infrastructure;

/// <summary>Violates: a module must not consume another module's DbContext.</summary>
public sealed class BetaRepository(AlphaDbContext context)
{
    public int Count => context.Entities.Count;
}
