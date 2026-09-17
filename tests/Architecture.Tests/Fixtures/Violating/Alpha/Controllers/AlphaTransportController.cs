using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Domain;
using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Controllers;

/// <summary>Violates: transport code must not reach into domain or infrastructure types.</summary>
public sealed class AlphaTransportController(AlphaRepository repository)
{
    public int Count() => repository.Count;

    public AlphaEntity? LastEntity { get; init; }
}
