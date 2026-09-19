using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// Scoring must be deterministic and non-fuzzy: exact value matches, sets for ports and
/// grades, numbers compared numerically, and null never satisfied by an invented value.
/// </summary>
public sealed class NluOutputComparerTests
{
    [Theory]
    [InlineData("  Dell ", "dell")]
    [InlineData("Dell   Technologies", "dell technologies")]
    [InlineData("IPS", "ips")]
    public void Text_comparison_trims_collapses_whitespace_and_folds_case(string expected, string actual)
    {
        var comparison = NluOutputComparer.Compare(
            BenchmarkFixtures.NoFilterSearch() with { Brand = expected },
            BenchmarkFixtures.NoFilterSearch() with { Brand = actual });

        Assert.Contains("brand", comparison.MatchedFields);
        Assert.Empty(comparison.Mismatches);
    }

    [Theory]
    [InlineData("1920×1080", "1920x1080")]
    [InlineData("1920 x 1080", "1920X1080")]
    public void Resolution_treats_multiplication_signs_and_spaces_as_the_same_notation(string expected, string actual)
    {
        var comparison = NluOutputComparer.Compare(
            BenchmarkFixtures.NoFilterSearch() with { Resolution = expected },
            BenchmarkFixtures.NoFilterSearch() with { Resolution = actual });

        Assert.Contains("resolution", comparison.MatchedFields);
    }

    [Fact]
    public void Ports_and_grades_are_compared_as_sets()
    {
        var expected = BenchmarkFixtures.NoFilterSearch() with
        {
            RequiredPorts = ["HDMI", "DisplayPort"],
            Grades = ["A", "B"],
        };
        var actual = BenchmarkFixtures.NoFilterSearch() with
        {
            RequiredPorts = ["displayport", "HDMI", "HDMI"],
            Grades = ["b", "a"],
        };

        var comparison = NluOutputComparer.Compare(expected, actual);

        Assert.Contains("requiredPorts", comparison.MatchedFields);
        Assert.Contains("grades", comparison.MatchedFields);
        Assert.Empty(comparison.Mismatches);
    }

    [Fact]
    public void A_missing_port_is_a_mismatch()
    {
        var expected = BenchmarkFixtures.NoFilterSearch() with { RequiredPorts = ["HDMI", "DisplayPort"] };
        var actual = BenchmarkFixtures.NoFilterSearch() with { RequiredPorts = ["HDMI"] };

        var comparison = NluOutputComparer.Compare(expected, actual);

        Assert.Contains(comparison.Mismatches, mismatch => mismatch.Field == "requiredPorts");
    }

    [Fact]
    public void Numbers_are_compared_numerically_not_textually()
    {
        var expected = BenchmarkFixtures.NoFilterSearch() with { BudgetType = "Hard", BudgetTarget = 3000m };
        var actual = BenchmarkFixtures.NoFilterSearch() with { BudgetType = "Hard", BudgetTarget = 3000.00m };

        Assert.Empty(NluOutputComparer.Compare(expected, actual).Mismatches);
    }

    [Fact]
    public void Null_never_matches_a_populated_value()
    {
        var expected = BenchmarkFixtures.NoFilterSearch();
        var actual = BenchmarkFixtures.NoFilterSearch() with { ModelCode = "P2419H" };

        var comparison = NluOutputComparer.Compare(expected, actual);

        Assert.Contains(comparison.Mismatches, mismatch => mismatch.Field == "modelCode");
        Assert.Contains("expected null", comparison.Mismatches[0].Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_omitted_optional_field_matches_an_expected_null()
    {
        var expected = BenchmarkFixtures.NoFilterSearch();
        var actual = new NluOutput { Intent = "ProductSearch", RequiredPorts = [], Grades = [], BudgetType = "None" };

        Assert.Empty(NluOutputComparer.Compare(expected, actual).Mismatches);
    }

    [Fact]
    public void A_missing_reply_never_matches_anything()
    {
        var comparison = NluOutputComparer.Compare(BenchmarkFixtures.NoFilterSearch(), actual: null);

        Assert.Equal(NluContract.Fields.Count, comparison.Mismatches.Length);
        Assert.Empty(comparison.MatchedFields);
    }

    [Fact]
    public void Intent_comparison_ignores_only_case_and_outer_whitespace()
    {
        var expected = BenchmarkFixtures.NoFilterSearch("ProductSearch");

        Assert.Empty(NluOutputComparer.Compare(expected, BenchmarkFixtures.NoFilterSearch(" productsearch ")).Mismatches);
        Assert.Contains(
            NluOutputComparer.Compare(expected, BenchmarkFixtures.NoFilterSearch("ProductDetails")).Mismatches,
            mismatch => mismatch.Field == "intent");
    }

    [Fact]
    public void Mismatch_reasons_name_the_field_and_both_values()
    {
        var expected = BenchmarkFixtures.NoFilterSearch() with { Panel = "IPS" };
        var actual = BenchmarkFixtures.NoFilterSearch() with { Panel = "TN" };

        var mismatch = Assert.Single(NluOutputComparer.Compare(expected, actual).Mismatches);

        Assert.Equal("panel", mismatch.Field);
        Assert.Equal("panel: expected 'IPS', got 'TN'", mismatch.Describe());
    }
}
