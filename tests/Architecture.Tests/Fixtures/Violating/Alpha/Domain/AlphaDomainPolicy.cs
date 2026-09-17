using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Domain;

/// <summary>Violates: a domain layer must not depend on its own module's Infrastructure.</summary>
public sealed class AlphaDomainPolicy(AlphaRepository repository)
{
    public int Count => repository.Count;
}
