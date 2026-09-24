using WhatsAppMonitorAssistant.Benchmarks.Nlu;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;
using WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The regression safety check of Issue #32: the deterministic explicit-fact guardrail may only ever
/// change a field to the value the authored Issue #8 dataset already expects. This is deliberately not
/// a benchmark run and says nothing about general intent accuracy — the historical Issue #8 result
/// stays FAIL at 62.3 %. The dataset, its manifest and its reports are read through the existing
/// hash-verified fixture and never modified.
/// </summary>
public sealed class NluHistoricalSafetyTests
{
    [Fact]
    public async Task The_authored_expectations_survive_the_guardrail_unchanged()
    {
        Assert.Equal(60, BenchmarkFixtures.Dataset.Cases.Count);

        var conflicts = new List<string>();

        foreach (var testCase in BenchmarkFixtures.Dataset.Cases)
        {
            IAiNluClient client = ClientFor(BenchmarkFixtures.Serialize(testCase.Expected));

            var result = await client.AnalyzeAsync(
                testCase.Input,
                NluConversationContext.Empty,
                CancellationToken.None);

            if (result.Status != NluAnalysisStatus.Success || result.Interpretation is null)
            {
                conflicts.Add($"{testCase.Id}: the authored expectation is not a valid model reply");
                continue;
            }

            conflicts.AddRange(CompareFields(result.Interpretation, testCase.Expected)
                .Where(field => !field.Matches)
                .Select(field => $"{testCase.Id}: {field.Field}"));
        }

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task A_neutral_model_reply_gains_only_values_the_dataset_expects()
    {
        var conflicts = new List<string>();
        var actedOn = new List<string>();

        foreach (var testCase in BenchmarkFixtures.Dataset.Cases)
        {
            IAiNluClient client = ClientFor(ScriptedOllamaClient.Reply("Greeting"));

            var result = await client.AnalyzeAsync(
                testCase.Input,
                NluConversationContext.Empty,
                CancellationToken.None);

            Assert.Equal(NluAnalysisStatus.Success, result.Status);

            var fields = CompareFields(result.Interpretation!, testCase.Expected);
            var changed = fields.Where(field => field.Changed).ToList();

            if (changed.Count > 0)
            {
                actedOn.Add($"{testCase.Id}:{string.Join(",", changed.Select(field => field.Field))}");
            }

            conflicts.AddRange(changed
                .Where(field => !field.Matches)
                .Select(field => $"{testCase.Id}: {field.Field}"));
        }

        Assert.Empty(conflicts);
        Assert.NotEmpty(actedOn);

        // The guardrail must be demonstrably active on the documented representative families; the
        // complete acted-on set is intentionally not pinned, because it is an implementation snapshot.
        var actedOnCaseIds = actedOn.Select(entry => entry.Split(':')[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("PS-001", actedOnCaseIds);
        Assert.Contains("BUD-HARD-001", actedOnCaseIds);
        Assert.Contains("BUD-SOFT-001", actedOnCaseIds);
        Assert.Contains("BUD-SOFT-003", actedOnCaseIds);
        Assert.Contains("BIO-001", actedOnCaseIds);
    }

    private static IAiNluClient ClientFor(string reply)
    {
        var transport = ScriptedOllamaClient.Transport(
            OllamaChatTransportResult.ReplyReceived(reply),
            OllamaChatTransportResult.ReplyReceived(reply));

        return ScriptedOllamaClient.CreateClient(transport);
    }

    private static List<FieldResult> CompareFields(NluInterpretation actual, NluOutput expected) =>
    [
        Scalar("intent", actual.Intent.ToString(), expected.Intent, nameof(NluIntent.Greeting)),
        Scalar("brand", actual.Brand, expected.Brand, null),
        Scalar("modelCode", actual.ModelCode, expected.ModelCode, null),
        Number("sizeInches", actual.SizeInches, expected.SizeInches, null),
        Scalar("panel", actual.Panel, expected.Panel, null),
        Scalar("resolution", actual.Resolution, expected.Resolution, null),
        Number("minRefreshRate", actual.MinRefreshRate, expected.MinRefreshRate, null),
        Sequence("requiredPorts", actual.RequiredPorts, expected.RequiredPorts ?? [], []),
        Sequence("grades", actual.Grades, expected.Grades ?? [], []),
        Scalar("budgetType", actual.BudgetType.ToString(), expected.BudgetType, nameof(NluBudgetType.None)),
        Number("budgetTarget", actual.BudgetTarget, expected.BudgetTarget, null),
        Number("budgetMin", actual.BudgetMin, expected.BudgetMin, null),
        Number("budgetMax", actual.BudgetMax, expected.BudgetMax, null),
        Scalar("useCase", actual.UseCase, expected.UseCase, null),
        Scalar("reference", actual.Reference, expected.Reference, null),
    ];

    private static FieldResult Scalar(string field, string? actual, string? expected, string? baseline) =>
        new(
            field,
            string.Equals(actual, expected, StringComparison.Ordinal),
            !string.Equals(actual, baseline, StringComparison.Ordinal));

    private static FieldResult Number<T>(string field, T? actual, T? expected, T? baseline)
        where T : struct =>
        new(field, Nullable.Equals(actual, expected), !Nullable.Equals(actual, baseline));

    private static FieldResult Sequence(
        string field,
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> baseline) =>
        new(field, actual.SequenceEqual(expected), !actual.SequenceEqual(baseline));

    private readonly record struct FieldResult(string Field, bool Matches, bool Changed);
}
