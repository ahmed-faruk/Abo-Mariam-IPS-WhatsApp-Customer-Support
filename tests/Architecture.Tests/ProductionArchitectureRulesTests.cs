using WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

namespace WhatsAppMonitorAssistant.Architecture.Tests;

/// <summary>
/// The module dependency rules from docs/TECHNICAL.md section 4, evaluated against the
/// real production assemblies. These must hold for every commit.
/// </summary>
public sealed class ProductionArchitectureRulesTests
{
    public static TheoryData<string> DocumentedRoots =>
    [
        "WhatsAppMonitorAssistant.BuildingBlocks",
        "WhatsAppMonitorAssistant.Modules.Catalog",
        "WhatsAppMonitorAssistant.Modules.Conversations",
        "WhatsAppMonitorAssistant.Modules.Identity",
        "WhatsAppMonitorAssistant.Modules.Intelligence",
        "WhatsAppMonitorAssistant.Modules.Messaging",
        "WhatsAppMonitorAssistant.Modules.Storefront",
    ];

    [Theory]
    [MemberData(nameof(DocumentedRoots))]
    public void Architecture_loader_sees_every_documented_root(string root)
    {
        var types = TestArchitectures.Production.Types
            .Where(type => type.FullName.StartsWith($"{root}.", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            types.Count > 0,
            $"No types were loaded for '{root}', so the boundary rules would pass without evaluating anything.");
    }

    [Fact]
    public void Domain_and_application_layers_do_not_depend_on_infrastructure() =>
        RuleAssertions.Holds(
            BoundaryRules.LayersMustNotDependOnOwnInfrastructure(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Modules_do_not_reference_another_modules_infrastructure() =>
        RuleAssertions.Holds(
            BoundaryRules.ModulesMustNotReferenceAnotherModulesInfrastructure(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Modules_do_not_consume_another_modules_db_context() =>
        RuleAssertions.Holds(
            BoundaryRules.ModulesMustNotConsumeAnotherModulesDbContext(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Cross_module_apis_use_contracts_only() =>
        RuleAssertions.Holds(
            BoundaryRules.CrossModuleApiMustUseContractsOnly(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Contracts_do_not_expose_persistence_types() =>
        RuleAssertions.Holds(
            BoundaryRules.ContractsMustNotExposePersistenceTypes(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Host_web_is_the_composition_root() =>
        RuleAssertions.Holds(
            BoundaryRules.HostWebIsTheCompositionRoot(ModuleBoundaries.Production),
            TestArchitectures.Production);

    [Fact]
    public void Transport_code_does_not_contain_business_rules() =>
        RuleAssertions.Holds(
            BoundaryRules.TransportMustNotContainBusinessRules(ModuleBoundaries.Production),
            TestArchitectures.Production);
}
