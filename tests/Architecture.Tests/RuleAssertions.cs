using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace WhatsAppMonitorAssistant.Architecture.Tests;

/// <summary>Shared assertions for architecture rule evaluation.</summary>
internal static class RuleAssertions
{
    public static void Holds(IArchRule rule, ArchUnitArchitecture architecture)
    {
        var failures = Failures(rule, architecture);

        Assert.True(
            failures.Count == 0,
            $"Expected the rule to hold but {failures.Count} dependency violation(s) were reported:{Environment.NewLine}{Join(failures)}");
    }

    public static void Flags(IArchRule rule, ArchUnitArchitecture architecture, string expectedTypeName)
    {
        var failures = Failures(rule, architecture);

        Assert.True(
            failures.Count > 0,
            "Expected the rule to report a violation but every evaluated type passed.");

        Assert.True(
            failures.Any(failure => failure.Contains(expectedTypeName, StringComparison.Ordinal)),
            $"Expected a violation mentioning '{expectedTypeName}' but the rule reported:{Environment.NewLine}{Join(failures)}");
    }

    private static IReadOnlyList<string> Failures(IArchRule rule, ArchUnitArchitecture architecture) =>
        [.. rule.Evaluate(architecture).Where(result => !result.Passed).SelectMany(Describe)];

    private static IEnumerable<string> Describe(EvaluationResult result) =>
    [
        result.ToString(),
        result.EvaluatedObjectIdentifier.ToString(),
        result.EvaluatedObject is IType type ? type.FullName : string.Empty,
    ];

    private static string Join(IReadOnlyList<string> failures) => string.Join(Environment.NewLine, failures);
}
