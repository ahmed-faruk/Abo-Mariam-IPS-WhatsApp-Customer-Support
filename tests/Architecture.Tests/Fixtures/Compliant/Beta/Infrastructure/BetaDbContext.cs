using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Infrastructure;

/// <summary>Compliant: a module owns its own DbContext.</summary>
public sealed class BetaDbContext
{
    public IReadOnlyList<BetaAggregate> Aggregates { get; } = [];
}
