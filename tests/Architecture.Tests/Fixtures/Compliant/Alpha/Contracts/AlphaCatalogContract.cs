using WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Contracts;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha.Contracts;

/// <summary>Compliant: one module's Contracts may use another module's Contracts types.</summary>
/// <param name="ModelCode">Normalized model code.</param>
/// <param name="BetaProduct">Another module's contract DTO.</param>
public sealed record AlphaCatalogContract(string ModelCode, BetaProductDto BetaProduct);
