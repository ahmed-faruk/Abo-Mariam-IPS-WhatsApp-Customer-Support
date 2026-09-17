using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Infrastructure;

/// <summary>Compliant: Infrastructure may use its own domain types and its own DbContext.</summary>
public sealed class BetaRepository(BetaDbContext context)
{
    public int Count => context.Aggregates.Count;

    public BetaAggregate? Find(long id) => context.Aggregates.FirstOrDefault(aggregate => aggregate.Id == id);
}
