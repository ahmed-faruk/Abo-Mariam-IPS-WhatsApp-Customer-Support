using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Features;

/// <summary>Violates: cross-module calls must use Contracts, not another module's domain types.</summary>
public sealed class AlphaFeatureHandler(BetaEntity entity)
{
    public BetaEntity Entity { get; } = entity;
}
