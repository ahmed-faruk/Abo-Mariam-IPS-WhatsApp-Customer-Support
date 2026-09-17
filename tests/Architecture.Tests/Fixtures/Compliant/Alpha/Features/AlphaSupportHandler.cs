using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Support.Application;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Features;

/// <summary>Compliant: a module may depend on the shared BuildingBlocks support abstractions.</summary>
public sealed class AlphaSupportHandler(ISharedClock clock)
{
    public DateTimeOffset Timestamp => clock.UtcNow;
}
