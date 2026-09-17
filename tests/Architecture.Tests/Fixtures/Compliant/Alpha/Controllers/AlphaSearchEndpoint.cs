using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Contracts;
using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Contracts;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Controllers;

/// <summary>Compliant: transport code depends on Contracts only.</summary>
public sealed class AlphaSearchEndpoint(AlphaProductDto alphaProduct, BetaProductDto betaProduct)
{
    public decimal Price => alphaProduct.Price + betaProduct.Price;
}
