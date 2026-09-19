using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// The committed benchmark inputs are part of the evidence, so their authoring rules are
/// asserted: exactly 60 cases, unique ids, full coverage, a dedicated hard-budget subset and
/// expected outputs that satisfy the schema.
/// </summary>
public sealed class BenchmarkDatasetTests
{
    [Fact]
    public void Committed_inputs_satisfy_every_authoring_rule() =>
        Assert.Empty(BenchmarkFixtures.ValidateCommittedInputs());

    [Fact]
    public void Dataset_contains_exactly_sixty_cases_with_unique_ids()
    {
        Assert.Equal(60, BenchmarkFixtures.Dataset.Cases.Count);
        Assert.Equal(60, BenchmarkFixtures.Dataset.Cases.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Case_order_is_the_file_order_so_latency_samples_are_reproducible()
    {
        var ids = BenchmarkFixtures.Dataset.Cases.Select(testCase => testCase.Id).ToArray();

        Assert.Equal("PS-001", ids[0]);
        Assert.Equal("AMB-003", ids[^1]);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Manifest_hash_and_count_match_the_committed_dataset()
    {
        Assert.Equal(BenchmarkFixtures.Manifest.Dataset.Sha256, BenchmarkFixtures.Dataset.Sha256);
        Assert.Equal(BenchmarkFixtures.Manifest.Dataset.CaseCount, BenchmarkFixtures.Dataset.Cases.Count);
        Assert.Equal(60, BenchmarkFixtures.Manifest.Dataset.CaseCount);
    }

    [Fact]
    public void Manifest_records_the_source_of_truth_thresholds_verbatim()
    {
        var gates = BenchmarkFixtures.Manifest.AcceptanceGates;

        Assert.Equal(90m, gates.IntentAccuracyPercent);
        Assert.Equal(100m, gates.HardBudgetAccuracyPercent);
        Assert.Equal(98m, gates.SchemaSuccessPercent);
        Assert.Equal(8m, gates.WarmMedianSeconds);
        Assert.Equal(12m, gates.WarmP95Seconds);
        Assert.Equal("one-retry-maximum", BenchmarkFixtures.Manifest.DefaultCandidate.RetryPolicy);
        Assert.Equal("qwen3.5:2b-q4_K_M", BenchmarkFixtures.Manifest.DefaultCandidate.Model);
        Assert.Equal(0, BenchmarkFixtures.Manifest.DefaultCandidate.Temperature);
        Assert.Equal(4096, BenchmarkFixtures.Manifest.DefaultCandidate.ContextTokens);
        Assert.Equal(20, BenchmarkFixtures.Manifest.DefaultCandidate.TimeoutSeconds);
    }

    [Theory]
    [InlineData("egyptian-arabic")]
    [InlineData("mixed-arabic-english")]
    [InlineData("brand-alias")]
    [InlineData("arabic-indic-digits")]
    [InlineData("western-digits")]
    [InlineData("model-code")]
    [InlineData("size")]
    [InlineData("panel")]
    [InlineData("resolution")]
    [InlineData("refresh-rate")]
    [InlineData("ports")]
    [InlineData("grades")]
    [InlineData("budget-hard")]
    [InlineData("budget-soft")]
    [InlineData("budget-range")]
    [InlineData("budget-none")]
    [InlineData("use-case")]
    [InlineData("follow-up-reference")]
    [InlineData("prompt-injection")]
    [InlineData("ambiguous-observational")]
    [InlineData("intent-product-search")]
    [InlineData("intent-product-details")]
    [InlineData("intent-product-comparison")]
    [InlineData("intent-availability-check")]
    [InlineData("intent-price-check")]
    [InlineData("intent-business-info")]
    [InlineData("intent-greeting")]
    [InlineData("intent-human-handoff")]
    [InlineData("intent-out-of-scope")]
    [InlineData("faq-working-hours")]
    [InlineData("faq-address")]
    [InlineData("faq-delivery")]
    [InlineData("faq-payment")]
    [InlineData("faq-warranty")]
    [InlineData("faq-contact-phone")]
    [InlineData("faq-return-exchange")]
    public void Required_coverage_tag_is_present_in_the_dataset(string tag)
    {
        Assert.Contains(tag, BenchmarkFixtures.Manifest.RequiredCoverageTags);
        Assert.Contains(tag, BenchmarkFixtures.Dataset.Tags);
    }

    [Fact]
    public void Dedicated_hard_budget_subset_is_non_empty_and_all_hard()
    {
        var hardBudgetCases = BenchmarkFixtures.Dataset.Cases.Where(testCase => testCase.HardBudgetCase).ToList();

        Assert.Equal(5, hardBudgetCases.Count);
        Assert.All(hardBudgetCases, testCase => Assert.Equal("Hard", testCase.Expected.BudgetType));
        Assert.All(hardBudgetCases, testCase => Assert.NotNull(testCase.Expected.BudgetTarget));
    }

    [Fact]
    public void Every_expected_output_satisfies_the_benchmark_schema()
    {
        foreach (var testCase in BenchmarkFixtures.Dataset.Cases)
        {
            var errors = BenchmarkFixtures.SchemaValidator.Validate(BenchmarkFixtures.Serialize(testCase.Expected));

            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Every_expected_intent_is_a_documented_intent()
    {
        var intents = BenchmarkFixtures.Dataset.Cases
            .Select(testCase => testCase.Expected.Intent!)
            .Distinct(StringComparer.Ordinal);

        Assert.All(intents, intent => Assert.Contains(intent, NluContract.Intents));
    }

    [Fact]
    public void Observational_cases_are_excluded_from_gating_and_explained()
    {
        var observational = BenchmarkFixtures.Dataset.Cases.Where(testCase => !testCase.Gating).ToList();

        Assert.Equal(3, observational.Count);
        Assert.All(observational, testCase => Assert.Contains("ambiguous-observational", testCase.Tags));
        Assert.All(observational, testCase => Assert.Contains("OBSERVATIONAL", testCase.Notes, StringComparison.Ordinal));
    }

    [Fact]
    public void Arabic_indic_case_inputs_expect_the_western_value_they_denote()
    {
        var arabicIndicCases = BenchmarkFixtures.Dataset.Cases
            .Where(testCase => ArabicNumerals.ContainsNonAsciiDigits(testCase.Input))
            .ToList();

        Assert.Equal(6, arabicIndicCases.Count);
        Assert.All(
            arabicIndicCases,
            testCase =>
            {
                var expected = testCase.Expected;
                var numbers = new List<decimal>();

                if (expected.SizeInches is { } size)
                {
                    numbers.Add(size);
                }

                if (expected.MinRefreshRate is { } refresh)
                {
                    numbers.Add(refresh);
                }

                if (expected.BudgetTarget is { } target)
                {
                    numbers.Add(target);
                }

                if (expected.BudgetMin is { } min)
                {
                    numbers.Add(min);
                }

                if (expected.BudgetMax is { } max)
                {
                    numbers.Add(max);
                }

                foreach (var number in ArabicNumerals.ExtractIntegers(testCase.Input))
                {
                    Assert.Contains((decimal)number, numbers);
                }
            });
    }

    [Theory]
    [InlineData("P2419H", "P2419H")]
    [InlineData("٢٤", "24")]
    [InlineData("٣٥٠٠ جنيه", "3500 جنيه")]
    public void Arabic_numerals_normalize_digits_without_touching_other_characters(string input, string expected) =>
        Assert.Equal(expected, ArabicNumerals.ToWesternDigits(input));

    [Fact]
    public void A_hash_mismatch_is_rejected_rather_than_ignored()
    {
        var root = BenchmarkFixtures.CreateTempRepository();

        try
        {
            var manifestPath = Path.Combine(root, "benchmarks", "Issue8.NluBenchmark", "manifest.json");
            var tampered = File.ReadAllText(manifestPath).Replace(
                BenchmarkFixtures.Manifest.Dataset.Sha256,
                new string('0', 64),
                StringComparison.Ordinal);
            File.WriteAllText(manifestPath, tampered);

            var manifest = BenchmarkManifest.Load(RepositoryPaths.ForRepositoryRoot(root));
            var exception = Assert.Throws<BenchmarkDataException>(() => BenchmarkDataset.Load(
                RepositoryPaths.ForRepositoryRoot(root),
                manifest));

            Assert.Contains("does not match manifest hash", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_benchmark_project_is_listed_in_the_solution()
    {
        var solution = File.ReadAllText(Path.Combine(
            BenchmarkFixtures.RepositoryRoot,
            "WhatsAppMonitorAssistant.slnx"));

        Assert.Contains("benchmarks/Issue8.NluBenchmark/Issue8.NluBenchmark.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void Benchmark_input_paths_use_the_exact_case_of_the_tracked_files()
    {
        // macOS is case-insensitive, Linux (GitHub Actions) is not: "data/v1/cases.jsonl" loads
        // fine on the Intel Mac and then fails CI with "Dataset not found".
        var benchmarkRoot = BenchmarkFixtures.Paths.BenchmarkRoot;

        AssertExactCaseExists(benchmarkRoot, RepositoryPaths.DatasetRelativePath);
        AssertExactCaseExists(benchmarkRoot, "schemas/nlu-output.schema.json");
        AssertExactCaseExists(benchmarkRoot, "manifest.json");
        Assert.Equal(RepositoryPaths.DatasetRelativePath, BenchmarkFixtures.Manifest.Dataset.Path);
        Assert.EndsWith(
            Path.Combine("Data", "v1", "cases.jsonl"),
            BenchmarkFixtures.Paths.DatasetFile,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks the path segment by segment using the on-disk spelling, so a wrong-case segment is
    /// caught even on a case-insensitive filesystem.
    /// </summary>
    private static void AssertExactCaseExists(string root, string relativePath)
    {
        var directory = root;

        foreach (var segment in relativePath.Split('/'))
        {
            var entries = Directory
                .GetFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .ToArray();

            Assert.True(
                entries.Contains(segment, StringComparer.Ordinal),
                $"'{segment}' is not the on-disk spelling inside {directory}. Entries: {string.Join(", ", entries)}.");

            directory = Path.Combine(directory, segment);
        }
    }
}
