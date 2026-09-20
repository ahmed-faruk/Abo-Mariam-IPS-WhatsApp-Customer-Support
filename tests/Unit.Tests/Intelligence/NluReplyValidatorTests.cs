using System.Text.Json.Nodes;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// Validation is stricter than deserialization: it enforces the frozen schema, the canonical
/// vocabularies, the budget semantics and the null/empty-collection convention, and it rejects every
/// key the documented contract does not contain — including commercial facts the model must never
/// author.
/// </summary>
public sealed class NluReplyValidatorTests
{
    [Fact]
    public void A_complete_reply_maps_every_documented_field()
    {
        var reply = Reply();
        reply["intent"] = "ProductSearch";
        reply["brand"] = "Dell";
        reply["modelCode"] = "P2419H";
        reply["sizeInches"] = 24;
        reply["panel"] = "IPS";
        reply["resolution"] = "1920x1080";
        reply["minRefreshRate"] = 144;
        reply["requiredPorts"] = new JsonArray("HDMI", "DisplayPort");
        reply["grades"] = new JsonArray("A");
        reply["budgetType"] = "Range";
        reply["budgetMin"] = 2000;
        reply["budgetMax"] = 4000;
        reply["useCase"] = "Programming";
        reply["reference"] = "الأولى";

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));

        var interpretation = Assert.IsType<NluInterpretation>(validation.Interpretation);

        Assert.Equal(NluIntent.ProductSearch, interpretation.Intent);
        Assert.Equal("Dell", interpretation.Brand);
        Assert.Equal("P2419H", interpretation.ModelCode);
        Assert.Equal(24m, interpretation.SizeInches);
        Assert.Equal("IPS", interpretation.Panel);
        Assert.Equal("1920x1080", interpretation.Resolution);
        Assert.Equal(144, interpretation.MinRefreshRate);
        Assert.Equal(["HDMI", "DisplayPort"], interpretation.RequiredPorts);
        Assert.Equal(["A"], interpretation.Grades);
        Assert.Equal(NluBudgetType.Range, interpretation.BudgetType);
        Assert.Null(interpretation.BudgetTarget);
        Assert.Equal(2000m, interpretation.BudgetMin);
        Assert.Equal(4000m, interpretation.BudgetMax);
        Assert.Equal("Programming", interpretation.UseCase);
        Assert.Equal("الأولى", interpretation.Reference);
    }

    [Fact]
    public void Omitted_optional_scalars_are_valid_and_map_to_null()
    {
        var reply = new JsonObject
        {
            ["intent"] = "Greeting",
            ["requiredPorts"] = new JsonArray(),
            ["grades"] = new JsonArray(),
            ["budgetType"] = "None",
        };

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));

        var interpretation = Assert.IsType<NluInterpretation>(validation.Interpretation);

        Assert.Null(interpretation.Brand);
        Assert.Null(interpretation.ModelCode);
        Assert.Null(interpretation.SizeInches);
        Assert.Null(interpretation.Panel);
        Assert.Null(interpretation.Resolution);
        Assert.Null(interpretation.MinRefreshRate);
        Assert.Null(interpretation.BudgetTarget);
        Assert.Null(interpretation.BudgetMin);
        Assert.Null(interpretation.BudgetMax);
        Assert.Null(interpretation.UseCase);
        Assert.Null(interpretation.Reference);
    }

    [Fact]
    public void Empty_required_collections_are_valid_and_never_null()
    {
        var validation = NluReplyValidator.Validate(Json(Reply()));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));

        var interpretation = Assert.IsType<NluInterpretation>(validation.Interpretation);

        Assert.NotNull(interpretation.RequiredPorts);
        Assert.NotNull(interpretation.Grades);
        Assert.Empty(interpretation.RequiredPorts);
        Assert.Empty(interpretation.Grades);
    }

    [Theory]
    [InlineData("Greeting")]
    [InlineData("ProductSearch")]
    [InlineData("ProductDetails")]
    [InlineData("ProductComparison")]
    [InlineData("AvailabilityCheck")]
    [InlineData("PriceCheck")]
    [InlineData("BusinessInfo")]
    [InlineData("HumanHandoff")]
    [InlineData("UnsupportedMedia")]
    [InlineData("OutOfScope")]
    public void Every_canonical_intent_is_accepted(string intent)
    {
        var reply = Reply();
        reply["intent"] = intent;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));
        Assert.Equal(intent, validation.Interpretation?.Intent.ToString());
    }

    [Theory]
    [InlineData("product_search")]
    [InlineData("productsearch")]
    [InlineData("Product Search")]
    [InlineData("Clarification")]
    [InlineData("")]
    public void An_unknown_intent_is_rejected(string intent)
    {
        var reply = Reply();
        reply["intent"] = intent;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Null(validation.Interpretation);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.intent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("None", null, null, null)]
    [InlineData("Soft", 3000, null, null)]
    [InlineData("Hard", 2500, null, null)]
    [InlineData("Range", null, 2000, 4000)]
    public void Every_canonical_budget_type_is_accepted(
        string budgetType,
        int? target,
        int? min,
        int? max)
    {
        var reply = Reply();
        reply["budgetType"] = budgetType;
        reply["budgetTarget"] = target;
        reply["budgetMin"] = min;
        reply["budgetMax"] = max;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));
        Assert.Equal(budgetType, validation.Interpretation?.BudgetType.ToString());
    }

    [Theory]
    [InlineData("Cheap")]
    [InlineData("soft")]
    [InlineData("")]
    [InlineData("range")]
    public void An_unknown_budget_type_is_rejected(string budgetType)
    {
        var reply = Reply();
        reply["budgetType"] = budgetType;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.budgetType", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("price")]
    [InlineData("stock")]
    [InlineData("quantity")]
    [InlineData("available")]
    [InlineData("warranty")]
    [InlineData("workingHours")]
    [InlineData("delivery")]
    [InlineData("payment")]
    [InlineData("confidence")]
    public void An_undocumented_field_is_rejected(string field)
    {
        var reply = Reply();
        reply[field] = "anything";

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Null(validation.Interpretation);
        Assert.Contains(validation.Problems, problem => problem.Contains($"$.{field}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("requiredPorts")]
    [InlineData("grades")]
    [InlineData("budgetType")]
    public void A_missing_required_field_is_rejected(string field)
    {
        var reply = Reply();
        reply.Remove(field);

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Null(validation.Interpretation);
        Assert.Contains(validation.Problems, problem => problem.StartsWith($"$.{field}", StringComparison.Ordinal));
    }

    [Fact]
    public void Wrong_json_types_are_rejected()
    {
        var cases = new (string Field, JsonNode? Value)[]
        {
            ("intent", 5),
            ("brand", 42),
            ("sizeInches", "24"),
            ("minRefreshRate", "144"),
            ("minRefreshRate", 144.5),
            ("requiredPorts", "HDMI"),
            ("requiredPorts", null),
            ("grades", new JsonObject()),
            ("budgetType", 3),
            ("budgetTarget", "3000"),
        };

        foreach (var (field, value) in cases)
        {
            var reply = Reply();
            reply[field] = value;

            var validation = NluReplyValidator.Validate(Json(reply));

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Problems, problem => problem.StartsWith($"$.{field}", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("N/A")]
    public void An_empty_string_sentinel_is_rejected_for_an_absent_scalar(string value)
    {
        var reply = Reply();
        reply["brand"] = value;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.brand", StringComparison.Ordinal));
    }

    [Fact]
    public void Outer_whitespace_of_a_stated_value_is_trimmed()
    {
        var reply = Reply();
        reply["brand"] = "  Dell ";
        reply["requiredPorts"] = new JsonArray(" HDMI ");

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.True(validation.IsValid, string.Join("; ", validation.Problems));
        Assert.Equal("Dell", validation.Interpretation?.Brand);
        Assert.Equal(["HDMI"], validation.Interpretation?.RequiredPorts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-24)]
    public void A_non_positive_size_is_rejected(double size)
    {
        var reply = Reply();
        reply["sizeInches"] = size;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.sizeInches", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void A_non_positive_refresh_rate_is_rejected(double refreshRate)
    {
        var reply = Reply();
        reply["minRefreshRate"] = refreshRate;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.minRefreshRate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    public void A_payload_that_is_not_a_json_object_is_rejected(string payload)
    {
        var validation = NluReplyValidator.Validate(payload);

        Assert.False(validation.IsValid);
        Assert.Null(validation.Interpretation);
        Assert.NotEmpty(validation.Problems);
    }

    [Fact]
    public void A_soft_budget_requires_a_target_and_forbids_the_range_bounds()
    {
        var missingTarget = Reply();
        missingTarget["budgetType"] = "Soft";

        var missing = NluReplyValidator.Validate(Json(missingTarget));

        Assert.False(missing.IsValid);
        Assert.Contains(missing.Problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));

        var contradictory = Reply();
        contradictory["budgetType"] = "Soft";
        contradictory["budgetTarget"] = 3000;
        contradictory["budgetMin"] = 2000;

        var contradiction = NluReplyValidator.Validate(Json(contradictory));

        Assert.False(contradiction.IsValid);
        Assert.Contains(contradiction.Problems, problem => problem.StartsWith("$.budgetMin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hard_budget_requires_a_target()
    {
        var reply = Reply();
        reply["budgetType"] = "Hard";

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_requires_both_bounds_and_a_consistent_order()
    {
        var missingMax = Reply();
        missingMax["budgetType"] = "Range";
        missingMax["budgetMin"] = 2000;

        var missing = NluReplyValidator.Validate(Json(missingMax));

        Assert.False(missing.IsValid);
        Assert.Contains(missing.Problems, problem => problem.StartsWith("$.budgetMax", StringComparison.Ordinal));

        var reversed = Reply();
        reversed["budgetType"] = "Range";
        reversed["budgetMin"] = 4000;
        reversed["budgetMax"] = 2000;

        var reversedValidation = NluReplyValidator.Validate(Json(reversed));

        Assert.False(reversedValidation.IsValid);
        Assert.Contains(reversedValidation.Problems, problem => problem.StartsWith("$.budgetMax", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_forbids_a_target()
    {
        var reply = Reply();
        reply["budgetType"] = "Range";
        reply["budgetTarget"] = 3000;
        reply["budgetMin"] = 2000;
        reply["budgetMax"] = 4000;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Soft", 0)]
    [InlineData("Soft", -3000)]
    [InlineData("Hard", 0)]
    public void A_budget_must_be_positive(string budgetType, double target)
    {
        var reply = Reply();
        reply["budgetType"] = budgetType;
        reply["budgetTarget"] = target;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void No_budget_must_leave_every_budget_field_null()
    {
        var reply = Reply();
        reply["budgetTarget"] = 3000;

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.budgetTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void Collection_members_must_be_non_blank_strings()
    {
        var reply = Reply();
        reply["requiredPorts"] = new JsonArray("HDMI", " ", 5);

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.StartsWith("$.requiredPorts", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_name_paths_without_repeating_model_values()
    {
        const string money = "1234567";

        var reply = Reply();
        reply["price"] = decimal.Parse(money);
        reply["budgetTarget"] = decimal.Parse(money);

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Contains("$.price", StringComparison.Ordinal));
        Assert.All(validation.Problems, problem => Assert.DoesNotContain(money, problem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("hostile\nname\u0007", "$.hostile?name?")]
    [InlineData("", "$.")]
    public void A_hostile_unknown_property_name_is_sanitized_in_its_diagnostic(
        string field,
        string expectedPathPrefix)
    {
        var reply = Reply();
        reply[field] = "anything";

        var validation = NluReplyValidator.Validate(Json(reply));
        var problem = Assert.Single(
            validation.Problems,
            candidate => candidate.Contains("is not a field of the documented NLU contract", StringComparison.Ordinal));

        Assert.StartsWith(expectedPathPrefix, problem, StringComparison.Ordinal);
        Assert.True(problem.Length <= NluDiagnostics.MaxProblemLength);
        Assert.DoesNotContain('\n', problem);
        Assert.DoesNotContain('\r', problem);
        Assert.DoesNotContain('\u0007', problem);
    }

    [Fact]
    public void An_absurdly_long_unknown_property_name_produces_one_bounded_diagnostic()
    {
        var reply = Reply();
        reply[new string('x', 100_000)] = "anything";

        var validation = NluReplyValidator.Validate(Json(reply));

        Assert.False(validation.IsValid);
        Assert.All(
            validation.Problems,
            problem => Assert.True(problem.Length <= NluDiagnostics.MaxProblemLength));
        Assert.Contains(
            validation.Problems,
            problem => problem.Contains(
                "$." + new string('x', NluDiagnostics.MaxIdentifierLength) + "...",
                StringComparison.Ordinal));
    }

    private static JsonObject Reply() => new()
    {
        ["intent"] = "ProductSearch",
        ["brand"] = null,
        ["modelCode"] = null,
        ["sizeInches"] = null,
        ["panel"] = null,
        ["resolution"] = null,
        ["minRefreshRate"] = null,
        ["requiredPorts"] = new JsonArray(),
        ["grades"] = new JsonArray(),
        ["budgetType"] = "None",
        ["budgetTarget"] = null,
        ["budgetMin"] = null,
        ["budgetMax"] = null,
        ["useCase"] = null,
        ["reference"] = null,
    };

    private static string Json(JsonObject reply) => reply.ToJsonString();
}
