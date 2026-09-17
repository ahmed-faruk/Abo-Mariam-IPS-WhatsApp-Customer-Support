using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Features;

/// <summary>Violates: a feature/application layer must not depend on its own module's Infrastructure.</summary>
public sealed class AlphaCommandHandler(AlphaRepository repository)
{
    public int Execute() => repository.Count;
}
