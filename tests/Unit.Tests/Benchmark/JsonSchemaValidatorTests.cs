using System.Text.Json;
using WhatsAppMonitorAssistant.Benchmarks.Nlu;

namespace WhatsAppMonitorAssistant.Unit.Tests.Benchmark;

/// <summary>
/// The schema is the contract gate, so the validator must reject everything the documented
/// contract does not allow while accepting a minimal reply with omitted optional fields.
/// </summary>
public sealed class JsonSchemaValidatorTests
{
    private const string MinimalValid =
        """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""";

    [Fact]
    public void Minimal_documented_reply_is_valid() =>
        Assert.Empty(BenchmarkFixtures.SchemaValidator.Validate(MinimalValid));

    [Fact]
    public void Full_documented_reply_is_valid() =>
        Assert.Empty(BenchmarkFixtures.SchemaValidator.Validate(
            """
            {
              "intent": "ProductSearch",
              "brand": "Dell",
              "modelCode": "P2419H",
              "sizeInches": 24,
              "panel": "IPS",
              "resolution": "1920x1080",
              "minRefreshRate": 144,
              "requiredPorts": ["HDMI", "DisplayPort"],
              "grades": ["A"],
              "budgetType": "Hard",
              "budgetTarget": 3000,
              "budgetMin": null,
              "budgetMax": null,
              "useCase": "Programming",
              "reference": null
            }
            """));

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":[]}""");

        Assert.Contains(errors, error => error.Contains("required property 'grades'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("required property 'budgetType'", StringComparison.Ordinal));
    }

    [Fact]
    public void Wrong_type_is_rejected()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":["HDMI"],"grades":[],"budgetType":"None","sizeInches":"24"}""");

        Assert.Contains(errors, error => error.Contains("$.sizeInches", StringComparison.Ordinal));
    }

    [Fact]
    public void Fractional_refresh_rate_is_rejected_because_the_contract_says_integer()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None","minRefreshRate":144.5}""");

        Assert.Contains(errors, error => error.Contains("$.minRefreshRate", StringComparison.Ordinal));
    }

    [Fact]
    public void Invented_price_or_stock_key_is_rejected()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None","price":500,"stock":12}""");

        Assert.Contains(errors, error => error.Contains("'price' is not part of the documented contract", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'stock' is not part of the documented contract", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_budget_type_is_rejected_by_the_documented_enum()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"Cheap"}""");

        Assert.Contains(errors, error => error.Contains("$.budgetType", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_string_port_is_rejected()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate(
            """{"intent":"ProductSearch","requiredPorts":["HDMI",3],"grades":[],"budgetType":"None"}""");

        Assert.Contains(errors, error => error.Contains("$.requiredPorts[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_json_is_reported_as_a_schema_failure_not_a_crash()
    {
        var errors = BenchmarkFixtures.SchemaValidator.Validate("of course I can help you with that");

        Assert.Single(errors);
        Assert.Contains("not valid JSON", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_schema_keyword_fails_loudly()
    {
        using var document = JsonDocument.Parse(
            """{"type":"object","properties":{"intent":{"type":"string"}},"required":["intent"],"minProperties":1}""");

        var exception = Assert.Throws<BenchmarkDataException>(() => new JsonSchemaValidator(document.RootElement.Clone()));

        Assert.Contains("minProperties", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_schema_uses_only_supported_keywords()
    {
        _ = new JsonSchemaValidator(JsonDocument.Parse(File.ReadAllText(BenchmarkFixtures.Paths.SchemaFile)).RootElement.Clone());
    }
}
