using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The structured-NLU contract is frozen: the vocabularies, the prompt identity and the schema
/// identity are the measured Controlled Demo Candidate, so any drift is a new benchmark decision
/// rather than a refactor. These tests pin the production copy against the historical benchmark
/// artifact without making production depend on the benchmark project.
/// </summary>
public sealed class NluContractTests
{
    private const string FrozenPromptVersion = "nlu-system-prompt-v3";

    private const string FrozenPromptSha256 =
        "2139120c08b3ad01a5389f986ae6a4a7e884da372591a951a6efaea225237d3a";

    private const string FrozenSchemaSha256 =
        "fb9eacee28dcf31f6438fbe63092a8b48abb42cf5c592f4edd06874b2f1d4302";

    public static TheoryData<string> CommercialFields =>
    [
        "price",
        "stock",
        "quantity",
        "available",
        "availability",
        "warranty",
        "workingHours",
        "delivery",
        "payment",
    ];

    private static readonly string[] CommercialAuthorityFields =
    [
        "price",
        "stock",
        "quantity",
        "available",
        "availability",
        "warranty",
        "workingHours",
        "delivery",
        "payment",
    ];

    [Fact]
    public void Canonical_intents_cover_every_enum_member_in_documentation_order()
    {
        var enumNames = Enum.GetNames<NluIntent>();

        Assert.Equal(NluContract.Intents, enumNames);
        Assert.Equal(10, enumNames.Length);
    }

    [Fact]
    public void Canonical_budget_types_cover_every_enum_member()
    {
        Assert.Equal(NluContract.BudgetTypes, Enum.GetNames<NluBudgetType>());
    }

    [Fact]
    public void Documented_field_order_matches_the_structured_contract()
    {
        string[] expected =
        [
            "intent",
            "brand",
            "modelCode",
            "sizeInches",
            "panel",
            "resolution",
            "minRefreshRate",
            "requiredPorts",
            "grades",
            "budgetType",
            "budgetTarget",
            "budgetMin",
            "budgetMax",
            "useCase",
            "reference",
        ];

        Assert.Equal(expected, NluContract.Fields);
        Assert.Equal(["intent", "requiredPorts", "grades", "budgetType"], NluContract.RequiredFields);
    }

    [Fact]
    public void Interpretation_exposes_exactly_the_documented_fields()
    {
        var properties = typeof(NluInterpretation)
            .GetProperties()
            .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
            .ToArray();

        Assert.Equal(NluContract.Fields, properties);
    }

    [Theory]
    [MemberData(nameof(CommercialFields))]
    public void Commercial_authority_fields_are_never_part_of_the_contract(string field)
    {
        Assert.DoesNotContain(field, NluContract.Fields);
    }

    [Fact]
    public void Production_prompt_is_the_frozen_version_and_runtime_hash()
    {
        Assert.Equal(FrozenPromptVersion, NluContract.PromptVersion);
        Assert.Equal(NluContract.PromptVersion, NluSystemPrompt.Version);
        Assert.Equal(FrozenPromptSha256, IntelligenceTestFiles.Sha256OfUtf8(NluSystemPrompt.Text));
        Assert.Equal(NluContract.PromptSha256, FrozenPromptSha256);
    }

    [Fact]
    public void Production_prompt_runtime_bytes_match_the_historical_benchmark_prompt()
    {
        Assert.Equal(
            WhatsAppMonitorAssistant.Benchmarks.Nlu.NluSystemPrompt.Text,
            NluSystemPrompt.Text);
        Assert.Equal(
            IntelligenceTestFiles.Sha256OfUtf8(WhatsAppMonitorAssistant.Benchmarks.Nlu.NluSystemPrompt.Text),
            IntelligenceTestFiles.Sha256OfUtf8(NluSystemPrompt.Text));
    }

    [Fact]
    public void Production_schema_bytes_are_the_frozen_schema_document()
    {
        Assert.Equal(FrozenSchemaSha256, NluContract.SchemaSha256);
        Assert.Equal(
            FrozenSchemaSha256,
            IntelligenceTestFiles.Sha256OfFile(IntelligenceTestFiles.ProductionSchemaPath));
        Assert.Equal(
            FrozenSchemaSha256,
            IntelligenceTestFiles.Sha256OfFile(IntelligenceTestFiles.BenchmarkSchemaPath));
    }

    [Fact]
    public void The_embedded_schema_the_adapter_sends_is_the_frozen_schema()
    {
        var schema = NluOutputSchema.Load();

        Assert.Equal(FrozenSchemaSha256, schema.Sha256);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.Format["$schema"]?.GetValue<string>());
        Assert.False(schema.Format["additionalProperties"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public void The_sent_schema_contains_exactly_the_documented_fields()
    {
        var schema = NluOutputSchema.Load();
        var propertyNames = schema.Format["properties"]?
            .AsObject()
            .Select(property => property.Key)
            .ToArray() ?? [];

        Assert.Equal(NluContract.Fields, propertyNames);

        foreach (var field in CommercialAuthorityFields)
        {
            Assert.DoesNotContain(field, propertyNames);
        }
    }
}
