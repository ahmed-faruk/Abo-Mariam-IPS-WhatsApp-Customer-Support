using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The intent routing matrix of docs/TECHNICAL.md section 9. Every branch is asserted through the
/// renderer-neutral reply it produces, the module contract it called, and the UX state it left behind.
/// </summary>
public sealed class ConversationIntentRouterTests
{
    private const long ConversationId = 7;
    private const string Customer = "20100000001";

    [Fact]
    public async Task A_greeting_is_a_fixed_reply_that_changes_no_state()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(NluIntent.Greeting);

        Assert.Equal(ConversationResponseKind.Greeting, route.Intent.Kind);
        Assert.Equal(ConversationStateDocument.Empty, route.State);
        Assert.Empty(harness.Search.Queries);
    }

    [Fact]
    public async Task An_out_of_scope_message_is_a_fixed_reply_that_changes_no_state()
    {
        var harness = new RouterHarness();
        var state = StateWithShortlist((10, 21));

        var route = await harness.RouteAsync(NluIntent.OutOfScope, state: state);

        Assert.Equal(ConversationResponseKind.OutOfScope, route.Intent.Kind);
        Assert.Equal(state, route.State);
    }

    [Fact]
    public async Task A_product_search_maps_the_stated_filters_and_stores_only_identifiers()
    {
        var harness = new RouterHarness();
        harness.Search.Results =
        [
            ConversationSamples.Recommendation(10, 21, price: 2400),
            ConversationSamples.Recommendation(11, 25, price: 2600),
        ];

        var route = await harness.RouteAsync(
            NluIntent.ProductSearch,
            interpretation: ConversationSamples.Interpretation(
                NluIntent.ProductSearch,
                brand: "Dell",
                sizeInches: 24,
                panel: "IPS",
                resolution: "1920x1080",
                requiredPorts: ["HDMI"],
                grades: ["A"],
                budgetType: NluBudgetType.Soft,
                budgetTarget: 3000,
                useCase: "Programming"));

        var query = Assert.Single(harness.Search.Queries);

        Assert.Equal("Dell", query.Brand);
        Assert.Equal(24, query.SizeInches);
        Assert.Equal("IPS", query.PanelType);
        Assert.Equal(1920, query.MinResolutionWidth);
        Assert.Equal(1080, query.MinResolutionHeight);
        Assert.Equal(["HDMI"], query.RequiredPorts);
        Assert.Equal(["A"], query.Grades);
        Assert.Equal(BudgetType.Soft, query.Budget!.Type);
        Assert.Equal(3000, query.Budget.Target);
        Assert.Equal("Programming", query.UseCase);

        Assert.Equal(ConversationResponseKind.ProductSearchResults, route.Intent.Kind);
        Assert.Equal([10, 11], route.Intent.ModelIds);
        Assert.Equal([21, 25], route.Intent.VariantIds);

        // The shortlist is one-based in display order and the current reference is the first item.
        Assert.Equal([1, 2], route.State.Shortlist.Select(entry => entry.Position));
        Assert.Equal(10, route.State.LastModelId);
        Assert.Equal(21, route.State.LastVariantId);
        Assert.Equal("ProductSearch", route.State.LastIntent);
        Assert.Equal(BudgetType.Soft, route.State.LastFilters!.BudgetType);
        Assert.Equal(3000, route.State.LastFilters.BudgetTarget);
    }

    [Fact]
    public async Task A_hard_budget_ceiling_is_carried_through_unchanged()
    {
        var harness = new RouterHarness();

        await harness.RouteAsync(
            NluIntent.ProductSearch,
            interpretation: ConversationSamples.Interpretation(
                NluIntent.ProductSearch,
                budgetType: NluBudgetType.Hard,
                budgetTarget: 2500));

        var query = Assert.Single(harness.Search.Queries);

        Assert.Equal(BudgetType.Hard, query.Budget!.Type);
        Assert.Equal(2500, query.Budget.Target);
        Assert.Null(query.Budget.Min);
        Assert.Null(query.Budget.Max);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_is_a_deterministic_no_match()
    {
        var harness = new RouterHarness();
        harness.Search.Results = [];

        var route = await harness.RouteAsync(NluIntent.ProductSearch);

        Assert.Equal(ConversationResponseKind.NoMatch, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.NoMatchUnderFilters, route.Intent.ReasonCode);
        Assert.Empty(route.State.Shortlist);
        Assert.Null(route.State.LastModelId);
        Assert.Null(route.State.LastVariantId);
    }

    [Fact]
    public async Task A_stated_resolution_that_cannot_be_read_is_a_clarification_and_not_a_silent_filter_drop()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.ProductSearch,
            interpretation: ConversationSamples.Interpretation(
                NluIntent.ProductSearch,
                resolution: "full hd"));

        Assert.Equal(ConversationResponseKind.Clarification, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.ResolutionNotUnderstood, route.Intent.ReasonCode);
        Assert.Empty(harness.Search.Queries);
    }

    [Fact]
    public async Task An_exact_model_code_that_is_unknown_is_a_deterministic_no_match()
    {
        var harness = new RouterHarness();
        harness.Search.ModelCodeResult = null;

        var route = await harness.RouteAsync(
            NluIntent.ProductDetails,
            interpretation: ConversationSamples.Interpretation(NluIntent.ProductDetails, modelCode: "P2419H"));

        Assert.Equal(["P2419H"], harness.Search.LookedUpModelCodes);
        Assert.Equal(ConversationResponseKind.NoMatch, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.ModelCodeNotAvailable, route.Intent.ReasonCode);
    }

    [Fact]
    public async Task A_model_code_details_lookup_resolves_the_catalogue_item()
    {
        var harness = new RouterHarness();
        harness.Search.ModelCodeResult = ConversationSamples.Recommendation(10, 21);
        harness.Details.Variants[21] = ConversationSamples.Recommendation(10, 21);

        var route = await harness.RouteAsync(
            NluIntent.ProductDetails,
            interpretation: ConversationSamples.Interpretation(NluIntent.ProductDetails, modelCode: "P2419H"));

        Assert.Equal(ConversationResponseKind.ProductDetails, route.Intent.Kind);
        Assert.Equal(10, route.Intent.ModelId);
        Assert.Equal(21, route.Intent.VariantId);
        Assert.Equal(10, route.State.LastModelId);
        Assert.Equal(21, route.State.LastVariantId);
    }

    [Theory]
    [InlineData("second", 11, 25)]
    [InlineData("التانية", 11, 25)]
    [InlineData("first", 10, 21)]
    [InlineData("الأولى", 10, 21)]
    [InlineData("this one", 11, 25)]
    [InlineData("ده", 11, 25)]
    public async Task A_reference_resolves_the_stored_shortlist_position(
        string reference,
        long expectedModelId,
        long expectedVariantId)
    {
        var harness = new RouterHarness();
        harness.Details.Variants[expectedVariantId] = ConversationSamples.Recommendation(
            expectedModelId,
            expectedVariantId);

        var route = await harness.RouteAsync(
            NluIntent.PriceCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: reference),
            // The stored current reference is the second item, so "this one" can only pass by reading
            // the stored reference instead of falling back to the first position.
            state: StateWithShortlist((10, 21), (11, 25)) with { LastModelId = 11, LastVariantId = 25 });

        Assert.Equal(ConversationResponseKind.Price, route.Intent.Kind);
        Assert.Equal(expectedModelId, route.Intent.ModelId);
        Assert.Equal(expectedVariantId, route.Intent.VariantId);
        Assert.Equal(expectedModelId, route.State.LastModelId);
    }

    [Fact]
    public async Task The_current_facts_are_read_again_for_every_price_question()
    {
        var harness = new RouterHarness();
        harness.Details.Variants[21] = ConversationSamples.Recommendation(10, 21, price: 2750);

        await harness.RouteAsync(
            NluIntent.PriceCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: "first"),
            state: StateWithShortlist((10, 21)));

        Assert.Equal([21], harness.Details.RequestedVariantIds);
    }

    [Fact]
    public async Task A_stale_shortlist_entry_whose_variant_is_gone_is_a_deterministic_no_match()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.AvailabilityCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.AvailabilityCheck, reference: "first"),
            state: StateWithShortlist((10, 21)));

        Assert.Equal(ConversationResponseKind.NoMatch, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.ProductNoLongerAvailable, route.Intent.ReasonCode);
    }

    [Fact]
    public async Task An_unknown_reference_is_a_clarification()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.PriceCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.PriceCheck, reference: "the cheapest one"),
            state: StateWithShortlist((10, 21)));

        Assert.Equal(ConversationResponseKind.Clarification, route.Intent.Kind);
        Assert.Equal(ConversationReferenceReasons.UnresolvedReference, route.Intent.ReasonCode);
        Assert.Empty(harness.Details.RequestedVariantIds);
    }

    [Fact]
    public async Task An_unqualified_price_question_without_a_stored_reference_is_a_clarification()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.PriceCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.PriceCheck));

        Assert.Equal(ConversationResponseKind.Clarification, route.Intent.Kind);
        Assert.Equal(ConversationReferenceReasons.CurrentReferenceMissing, route.Intent.ReasonCode);
    }

    [Fact]
    public async Task An_unqualified_follow_up_uses_the_current_reference_and_never_the_first_position()
    {
        var harness = new RouterHarness();
        harness.Details.Variants[25] = ConversationSamples.Recommendation(11, 25);
        var state = StateWithShortlist((10, 21), (11, 25)) with { LastModelId = 11, LastVariantId = 25 };

        var route = await harness.RouteAsync(
            NluIntent.PriceCheck,
            interpretation: ConversationSamples.Interpretation(NluIntent.PriceCheck),
            state: state);

        Assert.Equal(ConversationResponseKind.Price, route.Intent.Kind);
        Assert.Equal(11, route.Intent.ModelId);
        Assert.Equal(25, route.Intent.VariantId);
    }

    [Fact]
    public async Task A_comparison_covers_the_current_shortlist_in_display_order()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.ProductComparison,
            state: StateWithShortlist((10, 21), (11, 25)));

        Assert.Equal(ConversationResponseKind.ProductComparison, route.Intent.Kind);
        Assert.Equal([10, 11], route.Intent.ModelIds);
        Assert.Equal([21, 25], route.Intent.VariantIds);
        Assert.Equal([1, 2], route.State.Shortlist.Select(entry => entry.Position));
    }

    [Fact]
    public async Task A_comparison_with_a_single_candidate_is_a_clarification()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.ProductComparison,
            state: StateWithShortlist((10, 21)));

        Assert.Equal(ConversationResponseKind.Clarification, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.ComparisonNeedsTwoCandidates, route.Intent.ReasonCode);
    }

    [Theory]
    [InlineData("مواعيدكم إيه؟", BusinessInfoKeyNames.WorkingHours)]
    [InlineData("فين المكان", BusinessInfoKeyNames.Address)]
    [InlineData("في توصيل", BusinessInfoKeyNames.Delivery)]
    public async Task A_business_question_resolves_its_canonical_storefront_key(string body, string key)
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.BusinessInfo,
            interpretation: ConversationSamples.Interpretation(NluIntent.BusinessInfo),
            body: body);

        Assert.Equal(ConversationResponseKind.BusinessInfo, route.Intent.Kind);
        Assert.Equal(key, route.Intent.StorefrontKey);

        // The stored answer is deliberately not read here, and therefore cannot be persisted either.
        Assert.DoesNotContain(key, route.State.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_business_question_outside_the_allowlist_is_a_clarification()
    {
        var harness = new RouterHarness();

        var route = await harness.RouteAsync(
            NluIntent.BusinessInfo,
            interpretation: ConversationSamples.Interpretation(NluIntent.BusinessInfo),
            body: "حاجة تانية خالص");

        Assert.Equal(ConversationResponseKind.Clarification, route.Intent.Kind);
        Assert.Equal(ConversationReasonCodes.BusinessInfoKeyNotResolved, route.Intent.ReasonCode);
        Assert.Null(route.Intent.StorefrontKey);
    }

    [Fact]
    public async Task No_successful_route_ever_stores_a_commercial_fact()
    {
        var harness = new RouterHarness();
        harness.Search.Results = [ConversationSamples.Recommendation(10, 21, price: 2750)];

        var search = await harness.RouteAsync(NluIntent.ProductSearch);

        Assert.DoesNotContain("2750", search.State.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("price", search.State.ToJson(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantity", search.State.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    private static ConversationStateDocument StateWithShortlist(params (long ModelId, long VariantId)[] candidates) =>
        ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist(candidates),
            LastModelId = candidates[0].ModelId,
            LastVariantId = candidates[0].VariantId,
        };

    /// <summary>The router with its two module contracts replaced by deterministic test data.</summary>
    private sealed class RouterHarness
    {
        internal FakeCatalogSearch Search { get; } = new();

        internal FakeCatalogProductDetails Details { get; } = new();

        internal Task<ConversationRoute> RouteAsync(
            NluIntent intent,
            NluInterpretation? interpretation = null,
            ConversationStateDocument? state = null,
            string? body = null) =>
            new ConversationIntentRouter(Search, Details).RouteAsync(
                ConversationId,
                Customer,
                body ?? "the customer message",
                NluAnalysisResult.Success(interpretation ?? ConversationSamples.Interpretation(intent)),
                state ?? ConversationStateDocument.Empty,
                CancellationToken.None);
    }
}
