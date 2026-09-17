using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Infrastructure;

/// <summary>Compliant: a module owns its own DbContext.</summary>
public sealed class AlphaDbContext
{
    public IReadOnlyList<AlphaAggregate> Aggregates { get; } = [];
}
