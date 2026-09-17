using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Features;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Contracts;

/// <summary>Violates: a contract must not reach another module's feature/application types.</summary>
public sealed class AlphaFeatureLeakingContract(BetaFeatureHandler handler)
{
    public BetaFeatureHandler Handler { get; } = handler;
}
