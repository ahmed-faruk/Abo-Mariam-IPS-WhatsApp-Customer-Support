using System.Text.RegularExpressions;

namespace WhatsAppMonitorAssistant.Architecture.Tests.Boundaries;

/// <summary>Builds the regular expressions used to select and forbid namespaces.</summary>
internal static class NamespacePatterns
{
    /// <summary>The root namespace and everything below it.</summary>
    public static string Descendants(string root) => $@"^{Regex.Escape(root)}(\.|$)";

    /// <summary>The named layers directly below a root namespace.</summary>
    public static string Layers(string root, params string[] layers) =>
        $@"^{Regex.Escape(root)}\.({string.Join("|", layers.Select(Regex.Escape))})(\.|$)";

    /// <summary>Everything below a root namespace except its Contracts namespace.</summary>
    public static string NonContractNamespace(string root) =>
        $@"^{Regex.Escape(root)}\.(?!Contracts(\.|$))";

    /// <summary>The Contracts namespace of a root and everything below it.</summary>
    public static string ContractsNamespace(string root) => $@"^{Regex.Escape(root)}\.Contracts(\.|$)";

    /// <summary>The union of several namespace patterns.</summary>
    public static string Union(IEnumerable<string> patterns) => string.Join("|", patterns);

    /// <summary>The domain and infrastructure namespaces of every given root.</summary>
    public static string PersistenceNamespaces(IEnumerable<string> roots) =>
        Union(roots.Select(root => Layers(root, ModuleBoundaries.LayerDomain, ModuleBoundaries.LayerInfrastructure)));
}
