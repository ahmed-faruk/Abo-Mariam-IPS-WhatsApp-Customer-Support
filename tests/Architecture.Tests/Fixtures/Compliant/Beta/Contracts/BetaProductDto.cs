namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta.Contracts;

/// <summary>Compliant: an immutable contract DTO that exposes no entity or persistence type.</summary>
/// <param name="ModelCode">Normalized model code.</param>
/// <param name="Price">Current selling price.</param>
public sealed record BetaProductDto(string ModelCode, decimal Price);
