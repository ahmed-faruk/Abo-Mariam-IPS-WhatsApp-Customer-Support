using WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

namespace WhatsAppMonitorAssistant.Architecture.Tests;

/// <summary>
/// The same rules, evaluated against a compliant sample module set. Proves the rules
/// report violations only for prohibited dependencies and not for legitimate ones
/// (module to own infrastructure, module to another module's contracts, transport to contracts).
/// </summary>
public sealed class CompliantFixtureArchitectureTests
{
    [Fact]
    public void Domain_and_application_layers_do_not_depend_on_infrastructure() =>
        RuleAssertions.Holds(
            BoundaryRules.LayersMustNotDependOnOwnInfrastructure(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Modules_do_not_reference_another_modules_infrastructure() =>
        RuleAssertions.Holds(
            BoundaryRules.ModulesMustNotReferenceAnotherModulesInfrastructure(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Modules_do_not_consume_another_modules_db_context() =>
        RuleAssertions.Holds(
            BoundaryRules.ModulesMustNotConsumeAnotherModulesDbContext(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Cross_module_apis_use_contracts_only() =>
        RuleAssertions.Holds(
            BoundaryRules.CrossModuleApiMustUseContractsOnly(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Contracts_do_not_expose_persistence_types() =>
        RuleAssertions.Holds(
            BoundaryRules.ContractsMustNotExposePersistenceTypes(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Host_web_is_the_composition_root() =>
        RuleAssertions.Holds(
            BoundaryRules.HostWebIsTheCompositionRoot(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);

    [Fact]
    public void Transport_code_does_not_contain_business_rules() =>
        RuleAssertions.Holds(
            BoundaryRules.TransportMustNotContainBusinessRules(ModuleBoundaries.CompliantFixtures),
            TestArchitectures.Fixtures);
}
