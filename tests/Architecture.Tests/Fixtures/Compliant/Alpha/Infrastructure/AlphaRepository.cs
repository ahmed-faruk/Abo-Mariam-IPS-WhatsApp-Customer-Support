using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Infrastructure;

/// <summary>Compliant: Infrastructure may use its own domain types and its own DbContext.</summary>
public sealed class AlphaRepository(AlphaDbContext context)
{
    public int Count => context.Aggregates.Count;

    public AlphaAggregate? Find(long id) => context.Aggregates.FirstOrDefault(aggregate => aggregate.Id == id);
}
