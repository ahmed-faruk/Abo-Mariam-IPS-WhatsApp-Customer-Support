namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Support.Application;

/// <summary>Compliant: a shared BuildingBlocks-style abstraction that modules may depend on.</summary>
public interface ISharedClock
{
    DateTimeOffset UtcNow { get; }
}
