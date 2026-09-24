using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The bounded deterministic explicit-fact normalization of docs/TECHNICAL.md section 8.5, observed
/// through the public <see cref="IAiNluClient"/>: the demo-critical facts the customer stated in the
/// text are enforced even when the schema-valid model reply missed or contradicted them, and every
/// non-success result stays exactly what it was. The expected values are the frozen Phase 0 criteria
/// of Issue #32, never recomputed from production vocabularies.
/// </summary>
public sealed class NluDeterministicNormalizationTests
{
    private const string Demo01Dell24 = "عندك ديل 24؟";

    [Fact]
    public async Task The_stated_Dell_brand_is_normalized_on_the_first_valid_reply()
    {
        var (result, transport) = await AnalyzeAsync(
            Demo01Dell24,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("Dell", result.Interpretation.Brand);
        Assert.Equal(1, transport.Attempts);
        Assert.Equal(2, transport.Requests[0]["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task The_stated_Dell_brand_is_normalized_on_the_corrected_retry_reply()
    {
        var (result, transport) = await AnalyzeAsync(
            Demo01Dell24,
            OllamaChatTransportResult.ReplyReceived("not json"),
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal("Dell", result.Interpretation!.Brand);
        Assert.Equal(2, transport.Attempts);
    }

    [Theory]
    [InlineData(Demo01Dell24)]
    [InlineData("في حدود 3000")]
    public async Task A_timeout_is_never_normalized_into_a_success(string message)
    {
        var (result, transport) = await AnalyzeAsync(message, OllamaChatTransportResult.TimedOut());

        Assert.Equal(NluAnalysisStatus.Timeout, result.Status);
        Assert.Null(result.Interpretation);
        Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData(Demo01Dell24)]
    [InlineData("في حدود 3000")]
    public async Task An_unavailable_runtime_is_never_normalized_into_a_success(string message)
    {
        var (result, transport) = await AnalyzeAsync(message, OllamaChatTransportResult.Unavailable());

        Assert.Equal(NluAnalysisStatus.AiUnavailable, result.Status);
        Assert.Null(result.Interpretation);
        Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData(Demo01Dell24)]
    [InlineData("في حدود 3000")]
    public async Task Two_invalid_replies_stay_invalid_model_output(string message)
    {
        var (result, transport) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived("not json"),
            OllamaChatTransportResult.ReplyReceived("still not json"));

        Assert.Equal(NluAnalysisStatus.InvalidModelOutput, result.Status);
        Assert.Null(result.Interpretation);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task An_already_cancelled_caller_token_still_throws_and_never_reaches_the_model()
    {
        var transport = ScriptedOllamaClient.Transport(
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));
        IAiNluClient client = ScriptedOllamaClient.CreateClient(transport);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.AnalyzeAsync(Demo01Dell24, NluConversationContext.Empty, cancelled.Token));

        Assert.Equal(0, transport.Attempts);
    }

    [Theory]
    [InlineData("عايز ديل 24", "ProductSearch")]
    [InlineData("DELL IPS", "ProductSearch")]
    [InlineData("عايز dell 27", "ProductSearch")]
    public async Task Every_approved_Dell_alias_token_becomes_the_canonical_brand(string message, string intent)
    {
        var (result, _) = await AnalyzeAsync(message, OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply(intent)));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal("Dell", result.Interpretation!.Brand);
    }

    [Theory]
    [InlineData("ديل ولا HP", "HP", "HP")]
    [InlineData("dell ولا hp؟", "HP", "HP")]
    public async Task A_message_that_also_names_the_model_brand_keeps_the_model_answer(
        string message,
        string modelBrand,
        string expected)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", brand: modelBrand)));

        Assert.Equal(expected, result.Interpretation!.Brand);
    }

    [Theory]
    [InlineData("الديل اللي قولتلي عليها لسه موجودة؟")]
    [InlineData("عايز حاجة بديل")]
    [InlineData("عندك شاشة؟")]
    public async Task The_guardrail_never_invents_a_brand_from_an_unmatched_token(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Null(result.Interpretation!.Brand);
    }

    [Fact]
    public async Task The_captured_DEMO_02_reply_gains_its_explicit_IPS_panel_and_keeps_its_ports()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز IPS وفي HDMI",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("ProductSearch", requiredPorts: ["HDMI"])));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("IPS", result.Interpretation.Panel);
        Assert.Equal(["HDMI"], result.Interpretation.RequiredPorts);
    }

    [Theory]
    [InlineData("عايز شاشة IPS", "TN", "IPS")]
    [InlineData("عايز شاشة ips", null, "IPS")]
    [InlineData("عايز شاشة OLED", null, "OLED")]
    [InlineData("عايز شاشة VA", null, "VA")]
    public async Task An_explicit_panel_token_overrides_the_model_panel(
        string message,
        string? modelPanel,
        string expected)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", panel: modelPanel)));

        Assert.Equal(expected, result.Interpretation!.Panel);
    }

    [Theory]
    [InlineData("عايز شاشة IPS ولا VA", "TN", "TN")]
    [InlineData("عايز شاشة للجيمنج", null, null)]
    [InlineData("عايز شاشة TNT", null, null)]
    [InlineData("عايز شاشة VAT", null, null)]
    public async Task An_ambiguous_or_absent_panel_token_leaves_the_model_panel_alone(
        string message,
        string? modelPanel,
        string? expected)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", panel: modelPanel)));

        Assert.Equal(expected, result.Interpretation!.Panel);
    }

    [Fact]
    public async Task The_captured_DEMO_03_reply_becomes_the_stated_soft_budget()
    {
        var (result, _) = await AnalyzeAsync(
            "في حدود 3000",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("PriceCheck", budgetType: "Hard", budgetTarget: 3000)));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal(NluBudgetType.Soft, result.Interpretation.BudgetType);
        Assert.Equal(3000, result.Interpretation.BudgetTarget);
        Assert.Null(result.Interpretation.BudgetMin);
        Assert.Null(result.Interpretation.BudgetMax);
    }

    [Fact]
    public async Task The_captured_DEMO_04_reply_becomes_the_stated_hard_ceiling()
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز أعدي 2500",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("PriceCheck", budgetType: "Hard", budgetTarget: 2500)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal(NluBudgetType.Hard, result.Interpretation.BudgetType);
        Assert.Equal(2500, result.Interpretation.BudgetTarget);
        Assert.Null(result.Interpretation.BudgetMin);
        Assert.Null(result.Interpretation.BudgetMax);
    }

    [Theory]
    [InlineData("Soft", 2500)]
    [InlineData("Range", 2000d, 3000d)]
    [InlineData("Hard", 2000)]
    public async Task The_stated_hard_ceiling_overrides_any_conflicting_model_budget(
        string modelBudgetType,
        double modelTarget,
        double? modelMax = null)
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز أعدي 2500",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "ProductSearch",
                    budgetType: modelBudgetType,
                    budgetTarget: modelBudgetType == "Range" ? null : (decimal)modelTarget,
                    budgetMin: modelBudgetType == "Range" ? (decimal)modelTarget : null,
                    budgetMax: modelBudgetType == "Range" ? (decimal)modelMax! : null)));

        Assert.Equal(NluBudgetType.Hard, result.Interpretation!.BudgetType);
        Assert.Equal(2500, result.Interpretation.BudgetTarget);
        Assert.Null(result.Interpretation.BudgetMin);
        Assert.Null(result.Interpretation.BudgetMax);
    }

    [Theory]
    [InlineData("في حدود ٣٠٠٠", "Soft", 3000)]
    [InlineData("حوالي 3000 جنيه", "Soft", 3000)]
    [InlineData("في حدود 50 جنيه", "Soft", 50)]
    [InlineData("بحد أقصى 4000", "Hard", 4000)]
    [InlineData("أقصى حاجة ٢٥٠٠", "Hard", 2500)]
    [InlineData("مايزدش عن 3000", "Hard", 3000)]
    [InlineData("ومش عايز أعدي 4000", "Hard", 4000)]
    [InlineData("عايز ديل 24 في حدود 3500", "Soft", 3500)]
    [InlineData("عايز 27 IPS ومش عايز أعدي 4000", "Hard", 4000)]
    public async Task Every_documented_budget_phrase_family_binds_its_own_number(
        string message,
        string expectedType,
        int expectedValue)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(
            expectedType == "Soft" ? NluBudgetType.Soft : NluBudgetType.Hard,
            result.Interpretation!.BudgetType);
        Assert.Equal(expectedValue, result.Interpretation.BudgetTarget);
    }

    [Theory]
    [InlineData("في حدود 3000 أو حوالي 4000")]
    [InlineData("مش عايز أعدي 2500 في حدود 3000")]
    [InlineData("في حدود كام؟")]
    [InlineData("في حدود")]
    [InlineData("في حدود 3,000")]
    [InlineData("في حدود 24")]
    [InlineData("في حدود 27 بوصة")]
    public async Task An_ambiguous_budget_phrase_leaves_the_model_budget_alone(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("ProductSearch", budgetType: "Hard", budgetTarget: 1234)));

        Assert.Equal(NluBudgetType.Hard, result.Interpretation!.BudgetType);
        Assert.Equal(1234, result.Interpretation.BudgetTarget);
    }

    [Theory]
    [InlineData("PriceCheck")]
    [InlineData("AvailabilityCheck")]
    [InlineData("ProductDetails")]
    [InlineData("BusinessInfo")]
    [InlineData("Greeting")]
    [InlineData("OutOfScope")]
    public async Task A_budget_only_search_becomes_ProductSearch_whatever_the_model_returned(string modelIntent)
    {
        var (result, _) = await AnalyzeAsync(
            "في حدود 3000",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(modelIntent, budgetType: "Hard", budgetTarget: 3000)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal(NluBudgetType.Soft, result.Interpretation.BudgetType);
        Assert.Equal(3000, result.Interpretation.BudgetTarget);
    }

    [Theory]
    [InlineData("PriceCheck")]
    [InlineData("AvailabilityCheck")]
    [InlineData("ProductDetails")]
    [InlineData("BusinessInfo")]
    [InlineData("Greeting")]
    [InlineData("OutOfScope")]
    public async Task A_hard_budget_only_search_becomes_ProductSearch_whatever_the_model_returned(string modelIntent)
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز أعدي 2500",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(modelIntent, budgetType: "Soft", budgetTarget: 2500)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal(NluBudgetType.Hard, result.Interpretation.BudgetType);
        Assert.Equal(2500, result.Interpretation.BudgetTarget);
    }

    [Theory]
    [InlineData("مش عايز أعدي 2500", "Greeting")]
    [InlineData("ميزانيتي حوالي ٢٥٠٠", "AvailabilityCheck")]
    [InlineData("عايز حاجة في حدود 3000", "PriceCheck")]
    [InlineData("عايز شاشة IPS مش عايز أعدي 4500", "Greeting")]
    public async Task A_refinement_without_another_request_is_a_product_search(string message, string modelIntent)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply(modelIntent)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
    }

    [Theory]
    [InlineData("بكام P2419H؟ مش عايز أعدي 3000", "PriceCheck")]
    [InlineData("سعر الأولى كام في حدود 3000؟", "PriceCheck")]
    [InlineData("لسه موجودة في حدود 3000؟", "AvailabilityCheck")]
    [InlineData("عايز أكلم حد مش عايز أعدي 3000", "HumanHandoff")]
    [InlineData("مواعيدكم إيه وفي حدود 3000", "BusinessInfo")]
    [InlineData("ديل 24 ولا ديل 27 في حدود 3000", "ProductDetails")]
    public async Task A_competing_request_keeps_the_model_intent_even_with_a_stated_budget(
        string message,
        string modelIntent)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply(modelIntent)));

        Assert.Equal(modelIntent, result.Interpretation!.Intent.ToString());
    }

    [Fact]
    public async Task A_price_question_without_a_budget_phrase_keeps_the_model_budget_untouched()
    {
        var (result, _) = await AnalyzeAsync(
            "سعر P2419H كام؟",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("PriceCheck", budgetType: "Hard", budgetTarget: 1234)));

        Assert.Equal(NluIntent.PriceCheck, result.Interpretation!.Intent);
        Assert.Equal(NluBudgetType.Hard, result.Interpretation.BudgetType);
        Assert.Equal(1234, result.Interpretation.BudgetTarget);
    }

    [Fact]
    public async Task The_captured_DEMO_01_reply_completes_to_Dell_24_ProductSearch()
    {
        var (result, _) = await AnalyzeAsync(
            Demo01Dell24,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("Dell", result.Interpretation.Brand);
        Assert.Equal(24, result.Interpretation.SizeInches);
    }

    [Theory]
    [InlineData("عايز Dell 24 بوصة", 24)]
    [InlineData("ديل ٢٧ IPS", 27)]
    [InlineData("عايز شاشة 24", 24)]
    [InlineData("ديل 10", 10)]
    [InlineData("ديل 60", 60)]
    public async Task A_standalone_size_with_positive_evidence_is_enforced(string message, int expected)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", sizeInches: 27)));

        Assert.Equal(expected, result.Interpretation!.SizeInches);
    }

    [Theory]
    [InlineData("P2419H")]
    [InlineData("1920x1080")]
    [InlineData("144Hz")]
    [InlineData("في حدود 3000")]
    [InlineData("مش عايز أعدي 2500")]
    [InlineData("الضمان 24 شهر")]
    [InlineData("فاتحين 24 ساعة")]
    [InlineData("عايز 20 قطعة")]
    [InlineData("عايز 20 شاشة")]
    [InlineData("السعر 30 جنيه")]
    [InlineData("السعر 24 بوصة")]
    [InlineData("عايز ديل 20 قطعة")]
    [InlineData("ديل 24 شهر ضمان")]
    [InlineData("ديل 60 Hz")]
    [InlineData("عايز 24")]
    [InlineData("عندكم HP 24؟")]
    [InlineData("ديل 23.8")]
    [InlineData("ديل 65")]
    [InlineData("ديل 9")]
    public async Task A_number_that_belongs_to_another_role_is_never_a_size(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", sizeInches: 27)));

        Assert.Equal(27, result.Interpretation!.SizeInches);
    }

    [Fact]
    public async Task Two_distinct_stated_sizes_are_ambiguous_and_keep_the_model_size()
    {
        var (result, _) = await AnalyzeAsync(
            "ديل 24 ولا ديل 27",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch", sizeInches: 27)));

        Assert.Equal(27, result.Interpretation!.SizeInches);
    }

    [Fact]
    public async Task A_size_and_a_budget_in_one_message_keep_their_own_numbers()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز ديل 24 في حدود 3500",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "PriceCheck",
                    sizeInches: 3500,
                    budgetType: "Hard",
                    budgetTarget: 24)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("Dell", result.Interpretation.Brand);
        Assert.Equal(24, result.Interpretation.SizeInches);
        Assert.Equal(NluBudgetType.Soft, result.Interpretation.BudgetType);
        Assert.Equal(3500, result.Interpretation.BudgetTarget);
    }

    [Fact]
    public async Task A_hard_ceiling_and_a_size_in_one_message_keep_their_own_numbers()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز 27 IPS ومش عايز أعدي 4000",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("PriceCheck", sizeInches: 4000)));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("IPS", result.Interpretation.Panel);
        Assert.Equal(27, result.Interpretation.SizeInches);
        Assert.Equal(NluBudgetType.Hard, result.Interpretation.BudgetType);
        Assert.Equal(4000, result.Interpretation.BudgetTarget);
    }

    [Theory]
    [InlineData("مواعيدكم إيه؟")]
    [InlineData("مواعيد العمل")]
    [InlineData("مواعيد الشغل")]
    [InlineData("بتفتحوا امتى؟")]
    [InlineData("فاتحين امتى؟")]
    public async Task An_explicit_shop_hours_question_becomes_BusinessInfo(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("Greeting")));

        Assert.Equal(NluIntent.BusinessInfo, result.Interpretation!.Intent);
    }

    [Theory]
    [InlineData("موعد التوصيل امتى؟")]
    [InlineData("معاد التسليم")]
    [InlineData("ميعاد الحجز")]
    [InlineData("مواعيد التوصيل")]
    [InlineData("السلام عليكم")]
    public async Task An_unrelated_appointment_message_is_never_promoted_to_BusinessInfo(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("Greeting")));

        Assert.Equal(NluIntent.Greeting, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_handoff_request_keeps_the_model_intent_even_with_shop_hours_words()
    {
        var (result, _) = await AnalyzeAsync(
            "مواعيدكم إيه؟",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("HumanHandoff")));

        Assert.Equal(NluIntent.HumanHandoff, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_stated_product_fact_blocks_the_shop_hours_correction()
    {
        var (result, _) = await AnalyzeAsync(
            "مواعيدكم إيه وعندك ديل 24؟",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task The_captured_DEMO_07_reply_keeps_its_AvailabilityCheck_intent_and_its_reference()
    {
        var (result, _) = await AnalyzeAsync(
            "الديل اللي قولتلي عليها لسه موجودة؟",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("AvailabilityCheck", reference: "the previous monitor")));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.AvailabilityCheck, result.Interpretation!.Intent);
        Assert.Equal("the previous monitor", result.Interpretation.Reference);
        Assert.Null(result.Interpretation.Brand);
    }

    [Fact]
    public async Task The_captured_DEMO_05_and_DEMO_10_replies_are_returned_unchanged()
    {
        var (price, _) = await AnalyzeAsync(
            "سعر الأولى كام؟",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("PriceCheck")));
        var (handoff, _) = await AnalyzeAsync(
            "عايز أكلم حد",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("HumanHandoff")));

        Assert.Equal(NluIntent.PriceCheck, price.Interpretation!.Intent);
        Assert.Null(price.Interpretation.Reference);
        Assert.Equal(NluIntent.HumanHandoff, handoff.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_filter_only_ProductDetails_reply_becomes_a_product_search()
    {
        // The live behaviour of the fresh Phase 0 run: the frozen model answered the filter-only
        // follow-up as ProductDetails while extracting both filters correctly. No model code and no
        // reference identify one item, so the structured result is a search refinement.
        var (result, _) = await AnalyzeAsync(
            "عايز IPS وفي HDMI",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("ProductDetails", panel: "IPS", requiredPorts: ["HDMI"])));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("IPS", result.Interpretation.Panel);
        Assert.Equal(["HDMI"], result.Interpretation.RequiredPorts);
    }

    [Theory]
    [InlineData("brand")]
    [InlineData("size")]
    [InlineData("resolution")]
    [InlineData("refresh")]
    [InlineData("grades")]
    [InlineData("useCase")]
    [InlineData("budget")]
    public async Task Every_catalogue_filter_field_makes_a_targetless_ProductDetails_a_search(string filter)
    {
        var reply = filter switch
        {
            "brand" => ScriptedOllamaClient.Reply("ProductDetails", brand: "Dell"),
            "size" => ScriptedOllamaClient.Reply("ProductDetails", sizeInches: 24),
            "resolution" => ScriptedOllamaClient.Reply("ProductDetails", resolution: "1920x1080"),
            "refresh" => ScriptedOllamaClient.Reply("ProductDetails", minRefreshRate: 144),
            "grades" => ScriptedOllamaClient.Reply("ProductDetails", grades: ["A"]),
            "useCase" => ScriptedOllamaClient.Reply("ProductDetails", useCase: "Gaming"),
            _ => ScriptedOllamaClient.Reply("ProductDetails", budgetType: "Soft", budgetTarget: 3000),
        };

        var (result, _) = await AnalyzeAsync(
            "عايز شاشة تناسبني",
            OllamaChatTransportResult.ReplyReceived(reply));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task Several_filter_fields_together_still_become_a_product_search()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز شاشة مناسبة",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "ProductDetails",
                    brand: "Dell",
                    sizeInches: 24,
                    panel: "IPS",
                    resolution: "1920x1080",
                    minRefreshRate: 75,
                    requiredPorts: ["HDMI", "DisplayPort"],
                    grades: ["A"],
                    useCase: "Programming")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_ProductDetails_reply_naming_a_model_code_stays_a_details_request()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز تفاصيل P2419H",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "ProductDetails",
                    brand: "Dell",
                    modelCode: "P2419H",
                    sizeInches: 24,
                    panel: "IPS")));

        Assert.Equal(NluIntent.ProductDetails, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_ProductDetails_reply_carrying_a_reference_stays_a_details_request()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز تفاصيل الأولى",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("ProductDetails", reference: "first", panel: "IPS")));

        Assert.Equal(NluIntent.ProductDetails, result.Interpretation!.Intent);
    }

    [Fact]
    public async Task A_ProductDetails_reply_with_no_constraint_at_all_is_left_alone()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز تفاصيل",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductDetails")));

        Assert.Equal(NluIntent.ProductDetails, result.Interpretation!.Intent);
    }

    [Theory]
    [InlineData("PriceCheck")]
    [InlineData("AvailabilityCheck")]
    [InlineData("BusinessInfo")]
    [InlineData("Greeting")]
    [InlineData("HumanHandoff")]
    [InlineData("OutOfScope")]
    public async Task The_filter_refinement_rule_touches_only_ProductDetails(string modelIntent)
    {
        var (result, _) = await AnalyzeAsync(
            "عايز IPS وفي HDMI",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(modelIntent, panel: "IPS", requiredPorts: ["HDMI"])));

        Assert.Equal(modelIntent, result.Interpretation!.Intent.ToString());
    }

    [Theory]
    [InlineData("🙂🙂")]
    [InlineData("؟؟؟")]
    [InlineData(
        "111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111")]
    public async Task A_message_with_nothing_to_enforce_returns_the_model_reply_unchanged(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("Greeting")));

        Assert.Equal(NluAnalysisStatus.Success, result.Status);
        Assert.Equal(NluIntent.Greeting, result.Interpretation!.Intent);
        Assert.Null(result.Interpretation.Brand);
        Assert.Null(result.Interpretation.SizeInches);
    }

    [Fact]
    public async Task A_stated_brand_normalized_by_the_guardrail_is_enough_for_a_ProductDetails_search()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز ديل",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductDetails")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("Dell", result.Interpretation.Brand);
    }

    [Fact]
    public async Task A_stated_panel_normalized_by_the_guardrail_is_enough_for_a_ProductDetails_search()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز IPS",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductDetails")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal("IPS", result.Interpretation.Panel);
    }

    [Fact]
    public async Task A_stated_size_normalized_by_the_guardrail_is_enough_for_a_ProductDetails_search()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز شاشة 24",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductDetails")));

        Assert.Equal(NluIntent.ProductSearch, result.Interpretation!.Intent);
        Assert.Equal(24, result.Interpretation.SizeInches);
    }

    [Theory]
    [InlineData("brand")]
    [InlineData("modelCode")]
    [InlineData("size")]
    [InlineData("panel")]
    [InlineData("resolution")]
    [InlineData("refresh")]
    [InlineData("ports")]
    [InlineData("grades")]
    [InlineData("useCase")]
    [InlineData("reference")]
    [InlineData("budget")]
    public async Task Shop_hours_do_not_override_any_structured_product_field(string field)
    {
        var reply = field switch
        {
            "brand" => ScriptedOllamaClient.Reply("PriceCheck", brand: "HP"),
            "modelCode" => ScriptedOllamaClient.Reply("PriceCheck", modelCode: "P2419H"),
            "size" => ScriptedOllamaClient.Reply("PriceCheck", sizeInches: 24),
            "panel" => ScriptedOllamaClient.Reply("PriceCheck", panel: "TN"),
            "resolution" => ScriptedOllamaClient.Reply("PriceCheck", resolution: "1920x1080"),
            "refresh" => ScriptedOllamaClient.Reply("PriceCheck", minRefreshRate: 144),
            "ports" => ScriptedOllamaClient.Reply("PriceCheck", requiredPorts: ["HDMI"]),
            "grades" => ScriptedOllamaClient.Reply("PriceCheck", grades: ["A"]),
            "useCase" => ScriptedOllamaClient.Reply("PriceCheck", useCase: "Gaming"),
            "reference" => ScriptedOllamaClient.Reply("PriceCheck", reference: "first"),
            _ => ScriptedOllamaClient.Reply("PriceCheck", budgetType: "Soft", budgetTarget: 3000),
        };

        var (result, _) = await AnalyzeAsync(
            "مواعيدكم إيه؟",
            OllamaChatTransportResult.ReplyReceived(reply));

        Assert.Equal(NluIntent.PriceCheck, result.Interpretation!.Intent);
    }

    [Theory]
    [InlineData("مش عايز ديل")]
    [InlineData("مش عاوز ديل")]
    [InlineData("مش محتاج ديل")]
    [InlineData("لا ديل")]
    public async Task A_negated_Dell_mention_is_not_a_stated_brand(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Brand);
    }

    [Theory]
    [InlineData("مش عايز IPS")]
    [InlineData("مش عاوز TN")]
    [InlineData("مش محتاج VA")]
    [InlineData("لا OLED")]
    public async Task A_negated_panel_mention_is_not_a_stated_panel(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Panel);
    }

    [Theory]
    [InlineData("ديل أو HP")]
    [InlineData("HP أو ديل")]
    [InlineData("ديل ولا HP")]
    [InlineData("Dell or HP")]
    public async Task A_Dell_mention_offered_as_an_alternative_is_not_forced(string message)
    {
        var (result, _) = await AnalyzeAsync(
            message,
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Brand);
    }

    [Fact]
    public async Task A_price_magnitude_is_never_read_as_a_screen_size()
    {
        var (result, _) = await AnalyzeAsync(
            "ديل 24 ألف",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "ProductSearch",
                    sizeInches: 27,
                    budgetType: "Hard",
                    budgetTarget: 24000)));

        Assert.Equal("Dell", result.Interpretation!.Brand);
        Assert.Equal(27, result.Interpretation.SizeInches);
        Assert.Equal(NluBudgetType.Hard, result.Interpretation.BudgetType);
        Assert.Equal(24000, result.Interpretation.BudgetTarget);
    }

    [Fact]
    public async Task A_negated_size_is_not_a_stated_size()
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز 24 بوصة",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply("ProductSearch", sizeInches: 27)));

        Assert.Equal(27, result.Interpretation!.SizeInches);
    }

    [Fact]
    public async Task A_negated_Dell_through_a_monitor_noun_is_not_a_stated_brand()
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز شاشة ديل",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Brand);
    }

    [Fact]
    public async Task A_negated_panel_through_a_monitor_noun_is_not_a_stated_panel()
    {
        var (result, _) = await AnalyzeAsync(
            "مش عايز شاشة IPS",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Panel);
    }

    [Fact]
    public async Task Two_competing_budget_amounts_keep_the_model_budget()
    {
        var (result, _) = await AnalyzeAsync(
            "في حدود 3000 أو 4000 جنيه",
            OllamaChatTransportResult.ReplyReceived(
                ScriptedOllamaClient.Reply(
                    "ProductSearch",
                    budgetType: "Range",
                    budgetMin: 3000,
                    budgetMax: 4000)));

        Assert.Equal(NluBudgetType.Range, result.Interpretation!.BudgetType);
        Assert.Equal(3000, result.Interpretation.BudgetMin);
        Assert.Equal(4000, result.Interpretation.BudgetMax);
        Assert.Null(result.Interpretation.BudgetTarget);
    }

    [Fact]
    public async Task A_panel_offered_as_an_alternative_is_not_a_stated_panel()
    {
        var (result, _) = await AnalyzeAsync(
            "عايز IPS ولا Mini-LED",
            OllamaChatTransportResult.ReplyReceived(ScriptedOllamaClient.Reply("ProductSearch")));

        Assert.Null(result.Interpretation!.Panel);
    }

    private static async Task<(NluAnalysisResult Result, ScriptedOllamaClient.ScriptedTransport Transport)>
        AnalyzeAsync(string message, params OllamaChatTransportResult[] replies)
    {
        var transport = ScriptedOllamaClient.Transport(replies);
        IAiNluClient client = ScriptedOllamaClient.CreateClient(transport);
        var result = await client.AnalyzeAsync(message, NluConversationContext.Empty, CancellationToken.None);

        return (result, transport);
    }
}
