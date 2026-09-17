using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Domain;
using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Contracts;

/// <summary>Violates: contracts must not expose domain entities or persistence types.</summary>
public sealed class AlphaLeakyContract
{
    public AlphaEntity? Entity { get; init; }

    public AlphaRepository? Repository { get; init; }
}
