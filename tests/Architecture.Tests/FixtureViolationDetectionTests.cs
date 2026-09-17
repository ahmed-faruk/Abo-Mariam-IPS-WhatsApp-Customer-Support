using WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

namespace WhatsAppMonitorAssistant.Architecture.Tests;

/// <summary>
/// Proves that every rule detects its prohibited dependency. The sample types live in
/// the fixture namespaces only; the production architecture is never affected by them.
/// </summary>
public sealed class FixtureViolationDetectionTests
{
    [Fact]
    public void Infrastructure_dependency_from_a_layer_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.LayersMustNotDependOnOwnInfrastructure(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "AlphaDomainPolicy");

    [Fact]
    public void Cross_module_infrastructure_dependency_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.ModulesMustNotReferenceAnotherModulesInfrastructure(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "AlphaInfrastructureBridge");

    [Fact]
    public void Another_modules_db_context_dependency_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.ModulesMustNotConsumeAnotherModulesDbContext(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "BetaRepository");

    [Fact]
    public void Cross_module_dependency_outside_contracts_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.CrossModuleApiMustUseContractsOnly(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "AlphaFeatureHandler");

    [Fact]
    public void Contract_exposing_domain_or_persistence_types_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.ContractsMustNotExposePersistenceTypes(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "AlphaLeakyContract");

    [Fact]
    public void Module_depending_on_the_host_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.HostWebIsTheCompositionRoot(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "BetaPolicy");

    [Fact]
    public void Transport_depending_on_business_types_is_detected() =>
        RuleAssertions.Flags(
            BoundaryRules.TransportMustNotContainBusinessRules(ModuleBoundaries.ViolatingFixtures),
            TestArchitectures.Fixtures,
            "AlphaTransportController");
}
