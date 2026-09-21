using System.Security.Cryptography;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Intelligence.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The single corrective message of the documented retry policy is the one place a model reply could
/// reach a second instruction, so its diagnostics are sanitized, capped per problem, capped in number
/// and capped in total size. The base prompt keeps its frozen identity through all of it.
/// </summary>
public sealed class NluDiagnosticsTests
{
    private const string FrozenPromptSha256 =
        "2139120c08b3ad01a5389f986ae6a4a7e884da372591a951a6efaea225237d3a";

    [Fact]
    public void The_diagnostic_limits_are_the_frozen_bounds()
    {
        Assert.Equal(200, NluDiagnostics.MaxProblemLength);
        Assert.Equal(48, NluDiagnostics.MaxIdentifierLength);
        Assert.Equal(8, NluDiagnostics.MaxCorrectionProblems);
        Assert.Equal(16, NluDiagnostics.MaxRetainedProblems);
        Assert.Equal(2048, NluDiagnostics.MaxCorrectionLength);
    }

    [Fact]
    public void A_problem_list_is_bounded_with_one_fixed_omission_summary()
    {
        var problems = Enumerable.Range(0, 500).Select(index => $"$.field{index}: is invalid").ToArray();

        var clamped = NluDiagnostics.ClampProblems(problems);

        Assert.Equal(NluDiagnostics.MaxRetainedProblems + 1, clamped.Count);
        Assert.Equal("$.field0: is invalid", clamped[0]);
        Assert.Equal("$.field15: is invalid", clamped[NluDiagnostics.MaxRetainedProblems - 1]);
        Assert.Equal(NluDiagnostics.OmittedProblemsSummary, clamped[^1]);
        Assert.Single(clamped, problem => problem == NluDiagnostics.OmittedProblemsSummary);
    }

    [Fact]
    public void A_short_problem_list_is_clamped_without_an_omission_summary()
    {
        var clamped = NluDiagnostics.ClampProblems(["$.intent: is invalid"]);

        Assert.Equal(["$.intent: is invalid"], clamped);
    }

    [Theory]
    [InlineData("price", "price")]
    [InlineData("workingHours", "workingHours")]
    [InlineData("with space", "with?space")]
    [InlineData("line\nbreak", "line?break")]
    [InlineData("bell\u0007", "bell?")]
    public void An_identifier_keeps_only_safe_characters(string identifier, string expected)
    {
        Assert.Equal(expected, NluDiagnostics.SanitizeIdentifier(identifier));
    }

    [Fact]
    public void An_overlong_identifier_is_elided_to_the_cap()
    {
        var sanitized = NluDiagnostics.SanitizeIdentifier(new string('a', 5_000));

        Assert.Equal(NluDiagnostics.MaxIdentifierLength + 3, sanitized.Length);
        Assert.EndsWith("...", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_diagnostic_still_appears_in_the_correction()
    {
        var correction = NluSystemPrompt.BuildCorrection(
            ["$.intent: is not one of the documented intent names"]);

        Assert.Contains("did not match the required JSON schema", correction, StringComparison.Ordinal);
        Assert.Contains("$.intent", correction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hostile_problem_cannot_add_lines_or_exceed_the_per_problem_cap()
    {
        var hostile = "line one\nline two\r\u0007" + new string('w', 5_000);
        var clamped = NluDiagnostics.ClampProblem(hostile);

        Assert.True(clamped.Length <= NluDiagnostics.MaxProblemLength);
        Assert.DoesNotContain('\n', clamped);
        Assert.DoesNotContain('\r', clamped);
        Assert.DoesNotContain('\u0007', clamped);
        Assert.DoesNotContain(new string('w', 500), clamped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_lists_at_most_the_capped_number_of_problems_with_a_fixed_summary()
    {
        var problems = Enumerable.Range(0, 50).Select(index => $"$.field{index}: is invalid").ToArray();

        var correction = NluSystemPrompt.BuildCorrection(problems);

        Assert.Contains("$.field0", correction, StringComparison.Ordinal);
        Assert.DoesNotContain("$.field8", correction, StringComparison.Ordinal);
        Assert.Contains(NluDiagnostics.OmittedProblemsSummary, correction, StringComparison.Ordinal);
        Assert.True(correction.Length <= NluDiagnostics.MaxCorrectionLength);
    }

    [Fact]
    public void A_correction_never_exceeds_the_total_cap_even_for_worst_case_problems()
    {
        var problems = Enumerable.Range(0, 40)
            .Select(index => new string('w', 5_000) + index)
            .ToArray();

        var correction = NluSystemPrompt.BuildCorrection(problems);

        Assert.True(correction.Length <= 2048);
        Assert.DoesNotContain(new string('w', 300), correction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_stays_bounded_when_there_are_no_problems_to_list()
    {
        var correction = NluSystemPrompt.BuildCorrection([]);

        Assert.Contains("did not match the required JSON schema", correction, StringComparison.Ordinal);
        Assert.True(correction.Length <= NluDiagnostics.MaxCorrectionLength);
    }

    [Fact]
    public void The_base_prompt_keeps_its_frozen_identity()
    {
        Assert.Equal("nlu-system-prompt-v3", NluSystemPrompt.Version);
        Assert.Equal(
            FrozenPromptSha256,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NluSystemPrompt.Text))));
    }
}
