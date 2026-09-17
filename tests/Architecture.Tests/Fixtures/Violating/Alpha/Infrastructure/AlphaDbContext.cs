using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

/// <summary>Fixture stand-in for a module DbContext (name matters for the boundary rules).</summary>
public sealed class AlphaDbContext
{
    public IReadOnlyList<AlphaEntity> Entities { get; } = [];
}
