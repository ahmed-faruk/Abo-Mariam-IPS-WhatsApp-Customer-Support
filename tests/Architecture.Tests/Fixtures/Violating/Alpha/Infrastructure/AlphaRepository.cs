using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha.Infrastructure;

/// <summary>Allowed: a module's Infrastructure may use its own domain and its own DbContext.</summary>
public sealed class AlphaRepository(AlphaDbContext context)
{
    public int Count => context.Entities.Count;
}
