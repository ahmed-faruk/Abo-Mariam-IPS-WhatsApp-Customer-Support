using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Contracts;
using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Domain;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Features;

/// <summary>Compliant: uses its own domain plus another module's Contracts namespace.</summary>
public sealed class BetaSearchHandler(BetaAggregate aggregate, AlphaProductDto alphaProduct)
{
    public long Id => aggregate.Id;

    public string ModelCode => alphaProduct.ModelCode;
}
