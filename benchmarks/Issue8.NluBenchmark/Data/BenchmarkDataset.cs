using System.Security.Cryptography;
using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// The versioned case file. Cases keep file order: the harness never shuffles, so case
/// ordering and therefore the latency sample are reproducible.
/// </summary>
public sealed class BenchmarkDataset
{
    private const string HardBudget = "Hard";
    private const string SoftBudget = "Soft";
    private const string RangeBudget = "Range";
    private const string NoBudget = "None";

    private BenchmarkDataset(string version, string sha256, IReadOnlyList<BenchmarkCase> cases)
    {
        Version = version;
        Sha256 = sha256;
        Cases = cases;
    }

    public string Version { get; }

    public string Sha256 { get; }

    public IReadOnlyList<BenchmarkCase> Cases { get; }

    public IReadOnlyList<string> Tags =>
        [.. Cases.SelectMany(testCase => testCase.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public static BenchmarkDataset Load(RepositoryPaths paths, BenchmarkManifest manifest)
    {
        if (!File.Exists(paths.DatasetFile))
        {
            throw new BenchmarkDataException($"Dataset not found at {paths.DatasetFile}.");
        }

        var raw = File.ReadAllBytes(paths.DatasetFile);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(raw));
        var text = System.Text.Encoding.UTF8.GetString(raw);
        var cases = new List<BenchmarkCase>();
        var lineNumber = 0;

        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            try
            {
                cases.Add(BenchmarkJson.Deserialize<BenchmarkCase>(trimmed));
            }
            catch (Exception exception) when (exception is JsonException or BenchmarkDataException)
            {
                throw new BenchmarkDataException(
                    $"Dataset {paths.DatasetFile} line {lineNumber} is not a valid benchmark case: {exception.Message}",
                    exception);
            }
        }

        if (cases.Count != manifest.Dataset.CaseCount)
        {
            throw new BenchmarkDataException(
                $"Dataset {paths.DatasetFile} contains {cases.Count} cases but the manifest declares "
                + $"{manifest.Dataset.CaseCount}.",
                new InvalidOperationException());
        }

        if (!string.Equals(sha256, manifest.Dataset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BenchmarkDataException(
                $"Dataset hash {sha256} does not match manifest hash {manifest.Dataset.Sha256}. "
                + "Update manifest.json after intentionally changing the dataset.");
        }

        return new BenchmarkDataset(manifest.Dataset.Version, sha256, cases);
    }

    /// <summary>Every authoring rule the committed dataset must satisfy. Empty means valid.</summary>
    public IReadOnlyList<string> Validate(BenchmarkManifest manifest)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var coveredTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var testCase in Cases)
        {
            var prefix = $"case '{testCase.Id}'";

            if (string.IsNullOrWhiteSpace(testCase.Id))
            {
                errors.Add("a case has a blank id");
            }
            else if (!ids.Add(testCase.Id))
            {
                errors.Add($"{prefix}: duplicate id");
            }

            if (string.IsNullOrWhiteSpace(testCase.Input))
            {
                errors.Add($"{prefix}: input must not be blank");
            }

            if (testCase.Tags.Length == 0)
            {
                errors.Add($"{prefix}: at least one coverage tag is required");
            }

            if (testCase.Tags.Distinct(StringComparer.Ordinal).Count() != testCase.Tags.Length)
            {
                errors.Add($"{prefix}: coverage tags must not repeat");
            }

            coveredTags.UnionWith(testCase.Tags);
            errors.AddRange(ValidateExpectedOutput(testCase, prefix));
        }

        foreach (var requiredTag in manifest.RequiredCoverageTags)
        {
            if (!coveredTags.Contains(requiredTag))
            {
                errors.Add($"no case is tagged '{requiredTag}'");
            }
        }

        var hardBudgetCases = Cases.Count(testCase => testCase.HardBudgetCase);

        if (hardBudgetCases == 0)
        {
            errors.Add("the dedicated hard-budget subset must not be empty");
        }

        if (Cases.Count(testCase => !testCase.Gating) == Cases.Count)
        {
            errors.Add("every case is marked non-gating; at least one gating case is required");
        }

        return errors;
    }

    private static IEnumerable<string> ValidateExpectedOutput(BenchmarkCase testCase, string prefix)
    {
        var expected = testCase.Expected;

        if (expected.Intent is null || !NluContract.Intents.Contains(expected.Intent, StringComparer.Ordinal))
        {
            yield return $"{prefix}: expected intent '{expected.Intent}' is not a documented intent";
        }

        if (expected.BudgetType is null || !NluContract.BudgetTypes.Contains(expected.BudgetType, StringComparer.Ordinal))
        {
            yield return $"{prefix}: expected budgetType '{expected.BudgetType}' is not a documented budget type";
        }

        if (expected.RequiredPorts is null)
        {
            yield return $"{prefix}: expected requiredPorts must be present (schema-required field)";
        }

        if (expected.Grades is null)
        {
            yield return $"{prefix}: expected grades must be present (schema-required field)";
        }

        if (testCase.HardBudgetCase && !string.Equals(expected.BudgetType, HardBudget, StringComparison.Ordinal))
        {
            yield return $"{prefix}: is tagged as a dedicated hard-budget case but budgetType is '{expected.BudgetType}'";
        }

        foreach (var error in ValidateBudgetShape(testCase, prefix))
        {
            yield return error;
        }

        if (ArabicNumerals.ContainsNonAsciiDigits(testCase.Input))
        {
            var expectedNumbers = new[]
            {
                expected.SizeInches,
                expected.BudgetTarget,
                expected.BudgetMin,
                expected.BudgetMax,
            }
                .Where(value => value is not null)
                .Select(value => (double)value!.Value)
                .ToList();

            if (expected.MinRefreshRate is not null)
            {
                expectedNumbers.Add(expected.MinRefreshRate.Value);
            }

            foreach (var number in ArabicNumerals.ExtractIntegers(testCase.Input))
            {
                if (!expectedNumbers.Any(value => Math.Abs(value - number) < 0.0001))
                {
                    yield return $"{prefix}: input contains Arabic-Indic number {number} "
                        + "but no expected numeric field carries that value";
                }
            }
        }
    }

    private static IEnumerable<string> ValidateBudgetShape(BenchmarkCase testCase, string prefix)
    {
        var expected = testCase.Expected;

        switch (expected.BudgetType)
        {
            case HardBudget:
            case SoftBudget:
                if (expected.BudgetTarget is null)
                {
                    yield return $"{prefix}: {expected.BudgetType} budget requires budgetTarget";
                }

                if (expected.BudgetMin is not null || expected.BudgetMax is not null)
                {
                    yield return $"{prefix}: {expected.BudgetType} budget must leave budgetMin and budgetMax null";
                }

                break;
            case RangeBudget:
                if (expected.BudgetMin is null || expected.BudgetMax is null)
                {
                    yield return $"{prefix}: Range budget requires budgetMin and budgetMax";
                }

                if (expected.BudgetTarget is not null)
                {
                    yield return $"{prefix}: Range budget must leave budgetTarget null";
                }

                if (expected.BudgetMin is not null && expected.BudgetMax is not null
                    && expected.BudgetMin > expected.BudgetMax)
                {
                    yield return $"{prefix}: Range budget min {expected.BudgetMin} exceeds max {expected.BudgetMax}";
                }

                break;
            case NoBudget:
                if (expected.BudgetTarget is not null || expected.BudgetMin is not null || expected.BudgetMax is not null)
                {
                    yield return $"{prefix}: budgetType None must leave every budget field null";
                }

                break;
            default:
                break;
        }
    }

}
