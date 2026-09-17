namespace WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

/// <summary>
/// Namespace boundaries the architecture rules are evaluated against.
/// <see cref="Production"/> describes the real modular monolith; the fixture instances
/// describe sample namespaces used to prove that the rules detect prohibited dependencies.
/// </summary>
internal sealed record ModuleBoundaries
{
    public const string LayerDomain = "Domain";
    public const string LayerApplication = "Application";
    public const string LayerFeatures = "Features";
    public const string LayerContracts = "Contracts";
    public const string LayerInfrastructure = "Infrastructure";

    /// <summary>Namespaces that behave like modules: own layers, own contracts, own rules.</summary>
    public required IReadOnlyList<string> ModuleRoots { get; init; }

    /// <summary>Shared namespaces that modules may depend on but that own no module boundary.</summary>
    public required IReadOnlyList<string> SupportRoots { get; init; }

    /// <summary>The composition root. Modules must never depend on it.</summary>
    public required string HostRoot { get; init; }

    /// <summary>Namespace pattern that identifies transport (HTTP/UI) code.</summary>
    public required string TransportNamespacePattern { get; init; }

    public IReadOnlyList<string> AllRoots => [.. ModuleRoots, .. SupportRoots];

    public static ModuleBoundaries Production { get; } = new()
    {
        ModuleRoots =
        [
            "WhatsAppMonitorAssistant.Modules.Catalog",
            "WhatsAppMonitorAssistant.Modules.Conversations",
            "WhatsAppMonitorAssistant.Modules.Identity",
            "WhatsAppMonitorAssistant.Modules.Intelligence",
            "WhatsAppMonitorAssistant.Modules.Messaging",
            "WhatsAppMonitorAssistant.Modules.Storefront",
        ],
        SupportRoots =
        [
            "WhatsAppMonitorAssistant.BuildingBlocks",
        ],
        HostRoot = "WhatsAppMonitorAssistant.Host.Web",
        TransportNamespacePattern =
            @"(^|\.)(Api|Endpoints|Controllers)(\.|$)|^WhatsAppMonitorAssistant\.Host\.Web\.(Admin|Health)(\.|$)",
    };

    public static ModuleBoundaries ViolatingFixtures { get; } = new()
    {
        ModuleRoots =
        [
            "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Alpha",
            "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Beta",
        ],
        SupportRoots = [],
        HostRoot = "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Violating.Host",
        TransportNamespacePattern = @"\.(Api|Endpoints|Controllers)(\.|$)",
    };

    public static ModuleBoundaries CompliantFixtures { get; } = new()
    {
        ModuleRoots =
        [
            "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Alpha",
            "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Beta",
        ],
        SupportRoots =
        [
            "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Support",
        ],
        HostRoot = "WhatsAppMonitorAssistant.Architecture.Tests.Fixtures.Compliant.Host",
        TransportNamespacePattern = @"^WhatsAppMonitorAssistant\.Architecture\.Tests\.Fixtures\.Compliant\..*\.Controllers(\.|$)",
    };
}
