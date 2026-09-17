using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Domain;
using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Contracts;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Features;

/// <summary>Compliant: uses its own domain plus another module's Contracts namespace.</summary>
public sealed class AlphaSearchHandler(AlphaAggregate aggregate, BetaProductDto betaProduct)
{
    public long Id => aggregate.Id;

    public string ModelCode => betaProduct.ModelCode;
}
