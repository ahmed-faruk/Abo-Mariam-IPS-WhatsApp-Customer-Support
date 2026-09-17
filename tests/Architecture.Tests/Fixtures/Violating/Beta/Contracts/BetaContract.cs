namespace WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta.Contracts;

/// <summary>Allowed: a plain immutable contract DTO.</summary>
/// <param name="ModelCode">Normalized model code.</param>
public sealed record BetaContract(string ModelCode);
