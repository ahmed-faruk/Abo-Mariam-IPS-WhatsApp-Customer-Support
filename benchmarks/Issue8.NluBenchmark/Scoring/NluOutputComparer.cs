using System.Globalization;
using System.Text;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>One deterministic field mismatch, with both values rendered for the report.</summary>
public sealed record FieldMismatch
{
    public required string Field { get; init; }

    public required string Expected { get; init; }

    public required string Actual { get; init; }

    public string Describe() => $"{Field}: expected {Expected}, got {Actual}";
}

public sealed record NluComparison
{
    public required string[] MatchedFields { get; init; }

    public required FieldMismatch[] Mismatches { get; init; }
}

/// <summary>
/// Deterministic, non-fuzzy comparison of the documented fields. Strings are compared after
/// trimming, collapsing internal whitespace runs and case folding; ports and grades are sets;
/// numbers are compared numerically; an absent optional field counts as null, and null never
/// matches a populated value.
/// </summary>
public static class NluOutputComparer
{
    public static NluComparison Compare(NluOutput expected, NluOutput? actual)
    {
        var matched = new List<string>();
        var mismatches = new List<FieldMismatch>();

        if (actual is null)
        {
            return new NluComparison
            {
                MatchedFields = [],
                Mismatches =
                [
                    .. NluContract.Fields.Select(field => new FieldMismatch
                    {
                        Field = field,
                        Expected = "a documented value",
                        Actual = "no structured output",
                    }),
                ],
            };
        }

        CompareText("intent", expected.Intent, actual?.Intent, Canonical, matched, mismatches);
        CompareText("brand", expected.Brand, actual?.Brand, Canonical, matched, mismatches);
        CompareText("modelCode", expected.ModelCode, actual?.ModelCode, Canonical, matched, mismatches);
        CompareNumber("sizeInches", expected.SizeInches, actual?.SizeInches, matched, mismatches);
        CompareText("panel", expected.Panel, actual?.Panel, Canonical, matched, mismatches);
        CompareText("resolution", expected.Resolution, actual?.Resolution, CanonicalResolution, matched, mismatches);
        CompareNumber(
            "minRefreshRate",
            expected.MinRefreshRate is null ? null : (decimal?)expected.MinRefreshRate.Value,
            actual?.MinRefreshRate is null ? null : (decimal?)actual.MinRefreshRate.Value,
            matched,
            mismatches);
        CompareSets("requiredPorts", expected.RequiredPorts, actual?.RequiredPorts, matched, mismatches);
        CompareSets("grades", expected.Grades, actual?.Grades, matched, mismatches);
        CompareText("budgetType", expected.BudgetType, actual?.BudgetType, Canonical, matched, mismatches);
        CompareNumber("budgetTarget", expected.BudgetTarget, actual?.BudgetTarget, matched, mismatches);
        CompareNumber("budgetMin", expected.BudgetMin, actual?.BudgetMin, matched, mismatches);
        CompareNumber("budgetMax", expected.BudgetMax, actual?.BudgetMax, matched, mismatches);
        CompareText("useCase", expected.UseCase, actual?.UseCase, Canonical, matched, mismatches);
        CompareText("reference", expected.Reference, actual?.Reference, Canonical, matched, mismatches);

        return new NluComparison { MatchedFields = [.. matched], Mismatches = [.. mismatches] };
    }

    /// <summary>Trim, collapse whitespace runs, case fold.</summary>
    public static string? Canonical(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolution keeps its digits but treats the multiplication sign and spaces around the
    /// separator as the same notation ("1920 x 1080" == "1920X1080").
    /// </summary>
    public static string? CanonicalResolution(string? value)
    {
        var canonical = Canonical(value);

        return canonical?
            .Replace("\u00d7", "x", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    public static string[] CanonicalSet(IEnumerable<string>? values) =>
        values is null
            ? []
            : [.. values.Select(Canonical).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static void CompareText(
        string field,
        string? expected,
        string? actual,
        Func<string?, string?> canonicalize,
        List<string> matched,
        List<FieldMismatch> mismatches)
    {
        if (string.Equals(canonicalize(expected), canonicalize(actual), StringComparison.Ordinal))
        {
            matched.Add(field);
            return;
        }

        mismatches.Add(new FieldMismatch
        {
            Field = field,
            Expected = Render(expected),
            Actual = Render(actual),
        });
    }

    private static void CompareNumber(
        string field,
        decimal? expected,
        decimal? actual,
        List<string> matched,
        List<FieldMismatch> mismatches)
    {
        if (expected == actual)
        {
            matched.Add(field);
            return;
        }

        mismatches.Add(new FieldMismatch
        {
            Field = field,
            Expected = Render(expected?.ToString(CultureInfo.InvariantCulture)),
            Actual = Render(actual?.ToString(CultureInfo.InvariantCulture)),
        });
    }

    private static void CompareSets(
        string field,
        IEnumerable<string>? expected,
        IEnumerable<string>? actual,
        List<string> matched,
        List<FieldMismatch> mismatches)
    {
        var expectedSet = expected is null ? null : CanonicalSet(expected);
        var actualSet = actual is null ? null : CanonicalSet(actual);

        if (expectedSet is not null && actualSet is not null && expectedSet.SequenceEqual(actualSet, StringComparer.Ordinal))
        {
            matched.Add(field);
            return;
        }

        mismatches.Add(new FieldMismatch
        {
            Field = field,
            Expected = RenderSet(expectedSet),
            Actual = RenderSet(actualSet),
        });
    }

    private static string Render(string? value) => value is null ? "null" : $"'{value}'";

    private static string RenderSet(string[]? values) =>
        values is null ? "null" : $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";
}
