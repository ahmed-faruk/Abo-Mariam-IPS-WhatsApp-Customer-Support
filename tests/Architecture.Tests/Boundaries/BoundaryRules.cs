using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

/// <summary>
/// The executable form of the module dependency rules in docs/TECHNICAL.md section 4.
/// Each rule is evaluated against the production architecture and against the sample
/// fixture namespaces in the Architecture.Tests.
/// </summary>
internal static class BoundaryRules
{
    /// <summary>Domain and application/feature layers must not depend on their own module's Infrastructure.</summary>
    public static IArchRule LayersMustNotDependOnOwnInfrastructure(ModuleBoundaries boundaries) =>
        Combine(
            boundaries.AllRoots,
            root => Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.Layers(
                    root,
                    ModuleBoundaries.LayerDomain,
                    ModuleBoundaries.LayerApplication,
                    ModuleBoundaries.LayerFeatures))
                .Should()
                .NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching(NamespacePatterns.Layers(root, ModuleBoundaries.LayerInfrastructure))));

    /// <summary>A module must not reference another module's Infrastructure.</summary>
    public static IArchRule ModulesMustNotReferenceAnotherModulesInfrastructure(ModuleBoundaries boundaries) =>
        Combine(
            boundaries.AllRoots,
            root => Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.Descendants(root))
                .Should()
                .NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching(NamespacePatterns.Union(Others(boundaries, root)
                        .Select(other => NamespacePatterns.Layers(other, ModuleBoundaries.LayerInfrastructure))))));

    /// <summary>A module must not consume another module's DbContext.</summary>
    public static IArchRule ModulesMustNotConsumeAnotherModulesDbContext(ModuleBoundaries boundaries) =>
        Combine(
            boundaries.AllRoots,
            root => Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.Union(
                    Others(boundaries, root)
                        .Append(boundaries.HostRoot)
                        .Select(NamespacePatterns.Descendants)))
                .Should()
                .NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching(NamespacePatterns.Descendants(root))
                    .And()
                    .HaveNameEndingWith("DbContext")));

    /// <summary>Cross-module APIs must use the approved Contracts namespaces only.</summary>
    public static IArchRule CrossModuleApiMustUseContractsOnly(ModuleBoundaries boundaries) =>
        Combine(
            boundaries.ModuleRoots,
            root => Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.NonContractNamespace(root))
                .Should()
                .NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching(NamespacePatterns.Union(Others(boundaries, root)
                        .Select(NamespacePatterns.NonContractNamespace)))));

    /// <summary>Contracts must not expose domain entities, persistence types or DbContexts.</summary>
    public static IArchRule ContractsMustNotExposePersistenceTypes(ModuleBoundaries boundaries)
    {
        var contracts = Types().That()
            .ResideInNamespaceMatching(NamespacePatterns.Union(
                boundaries.ModuleRoots.Select(NamespacePatterns.ContractsNamespace)));

        var noDomainOrInfrastructure = contracts
            .Should()
            .NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.PersistenceNamespaces(boundaries.ModuleRoots)));

        var noDbContext = Types().That()
            .ResideInNamespaceMatching(NamespacePatterns.Union(
                boundaries.ModuleRoots.Select(NamespacePatterns.ContractsNamespace)))
            .Should()
            .NotDependOnAny(Types().That().HaveNameEndingWith("DbContext"));

        return noDomainOrInfrastructure.And(noDbContext);
    }

    /// <summary>Host.Web is the composition root: modules must not depend on it.</summary>
    public static IArchRule HostWebIsTheCompositionRoot(ModuleBoundaries boundaries) =>
        Types().That()
            .ResideInNamespaceMatching(NamespacePatterns.Union(boundaries.AllRoots.Select(NamespacePatterns.Descendants)))
            .Should()
            .NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.Descendants(boundaries.HostRoot)));

    /// <summary>Transport code must not contain business rules: no domain or infrastructure reach-through.</summary>
    public static IArchRule TransportMustNotContainBusinessRules(ModuleBoundaries boundaries)
    {
        var transport = Types().That().ResideInNamespaceMatching(boundaries.TransportNamespacePattern);

        var noDomainOrInfrastructure = transport
            .Should()
            .NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(NamespacePatterns.PersistenceNamespaces(boundaries.AllRoots)));

        var noDbContext = Types().That()
            .ResideInNamespaceMatching(boundaries.TransportNamespacePattern)
            .Should()
            .NotDependOnAny(Types().That().HaveNameEndingWith("DbContext"));

        return noDomainOrInfrastructure.And(noDbContext);
    }

    private static IEnumerable<string> Others(ModuleBoundaries boundaries, string root) =>
        boundaries.AllRoots.Where(candidate => !string.Equals(candidate, root, StringComparison.Ordinal));

    private static IArchRule Combine(IEnumerable<string> roots, Func<string, IArchRule> create) =>
        roots.Select(create).Aggregate((combined, next) => combined.And(next));
}
