using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The deterministic renderer of docs/TECHNICAL.md section 12. Every reply is built from the current
/// Catalog and Storefront facts the renderer reads itself, the products a search reply displays are the
/// current results of the effective query it re-runs, and an impossible internal contract throws instead
/// of being answered with invented text.
/// </summary>
public sealed class DeterministicConversationRendererTests
{
    [Theory]
    [InlineData(ConversationResponseKind.Greeting)]
    [InlineData(ConversationResponseKind.HumanHandoff)]
    [InlineData(ConversationResponseKind.UnsupportedMedia)]
    [InlineData(ConversationResponseKind.OutOfScope)]
    [InlineData(ConversationResponseKind.AiUnavailable)]
    [InlineData(ConversationResponseKind.Clarification)]
    public async Task A_fixed_reply_is_application_owned_text_that_reads_no_fact(ConversationResponseKind kind)
    {
        var harness = new RendererHarness();
        var intent = harness.Intent(kind);

        var first = await harness.Renderer.RenderAsync(intent);
        var second = await harness.Renderer.RenderAsync(intent);

        Assert.True(first.IsRendered);
        Assert.Equal(first.Body, second.Body);
        Assert.Empty(first.DisplayedCandidates);

        // No commercial value can appear in a fixed reply, because nothing was read to build it.
        Assert.DoesNotContain("2500", first.Body, StringComparison.Ordinal);
        Assert.Empty(harness.Search.Queries);
        Assert.Empty(harness.Details.RequestedModelIds);
        Assert.Empty(harness.BusinessInfo.RequestedKeys);
    }

    [Fact]
    public async Task A_search_reply_displays_the_current_results_of_the_effective_query_it_was_given()
    {
        var harness = new RendererHarness();
        var query = new ProductSearchQuery
        {
            Brand = "Dell",
            SizeInches = 24,
            Budget = ProductBudget.Hard(2500),
        };

        harness.Search.OnSearch = received => received.Budget is { Type: BudgetType.Hard, Target: 2500m }
            ? [harness.Recommendation(10, 21, 2400), harness.Recommendation(11, 25, 2000)]
            : [];

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults).WithSearchQuery(query));

        Assert.True(rendered.IsRendered);
        Assert.Equal(
            [(10L, 21L), (11L, 25L)],
            rendered.DisplayedCandidates.Select(candidate => (candidate.ModelId, candidate.VariantId)));

        // The visible text is in the same order as the displayed identifiers, and it quotes the current
        // catalogue prices rather than anything the intent carried.
        Assert.Contains("Dell 10", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("2400", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("2000", rendered.Body, StringComparison.Ordinal);
        Assert.True(
            rendered.Body!.IndexOf("Dell 10", StringComparison.Ordinal)
            < rendered.Body.IndexOf("Dell 11", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_search_reply_displays_at_most_the_bounded_number_of_products()
    {
        var harness = new RendererHarness();
        harness.Search.Results = [.. Enumerable.Range(1, 8).Select(index => harness.Recommendation(index, index + 100))];

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults)
                .WithSearchQuery(new ProductSearchQuery { Brand = "Dell" }));

        Assert.Equal(DeterministicConversationRenderer.MaxDisplayedProducts, rendered.DisplayedCandidates.Count);
        Assert.Equal(
            Enumerable.Range(1, DeterministicConversationRenderer.MaxDisplayedProducts).Select(index => (long)index),
            rendered.DisplayedCandidates.Select(candidate => candidate.ModelId));
    }

    [Fact]
    public async Task A_search_whose_current_results_are_empty_displays_nothing_and_says_so()
    {
        var harness = new RendererHarness();
        harness.Search.Results = [];

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults)
                .WithSearchQuery(new ProductSearchQuery { Brand = "Dell" }));

        Assert.True(rendered.IsRendered);
        Assert.Empty(rendered.DisplayedCandidates);
        Assert.Contains("ملقتش", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_bounded_by_a_hard_ceiling_says_the_ceiling_is_why_nothing_matched()
    {
        var harness = new RendererHarness();
        harness.Search.Results = [];

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults)
                .WithSearchQuery(new ProductSearchQuery { Budget = ProductBudget.Hard(2500) }));

        // The reply names the ceiling instead of sounding like a generic failure, and it never offers a
        // price above it.
        Assert.Contains("الحد السعري", rendered.Body, StringComparison.Ordinal);
        Assert.Empty(rendered.DisplayedCandidates);
    }

    [Fact]
    public async Task PriceCheck_ReloadsCurrentPrice()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21));

        var intent = harness.Intent(ConversationResponseKind.Price) with { ModelId = 10, VariantId = 21 };

        var first = await harness.Renderer.RenderAsync(intent);

        Assert.Contains("2500", first.Body, StringComparison.Ordinal);

        // The catalogue price changes; the very same intent must answer the new value.
        harness.Details.Models[10] = harness.Details.Models[10] with
        {
            Variants = [ConversationSamples.Variant(21, isActive: true, quantity: 3) with { Price = 1999m }],
        };

        var second = await harness.Renderer.RenderAsync(intent);

        Assert.Contains("1999", second.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("2500", second.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renderer_NeverUsesModelAuthoredPrice()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.Model(
            10,
            isActive: true,
            ConversationSamples.Variant(21, isActive: true, quantity: 3) with { Price = 2750m }));

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.Price) with { ModelId = 10, VariantId = 21 });

        // The intent has no field a price could travel in, so the only price that can reach the reply is
        // the one the renderer read from the catalogue: the current 2750, never a default or a stale value.
        Assert.Contains("2750", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("2500", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_active_product_whose_current_quantity_is_zero_is_reported_as_unavailable()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21, quantity: 0));

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.Availability) with { ModelId = 10, VariantId = 21 });

        // The identity is still answerable; only its availability changed.
        Assert.True(rendered.IsRendered);
        Assert.Contains("غير متوفر", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("Dell 10", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_details_reply_reports_the_current_facts()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.Model(
            10,
            isActive: true,
            ConversationSamples.Variant(21, isActive: true, quantity: 3) with
            {
                Grade = "A",
                WarrantyDays = 90,
            }));

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductDetails) with { ModelId = 10, VariantId = 21 });

        Assert.Contains("Dell 10", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("M10", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("2500", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("90", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("A", rendered.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConversationResponseKind.ProductDetails)]
    [InlineData(ConversationResponseKind.Price)]
    [InlineData(ConversationResponseKind.Availability)]
    public async Task A_product_that_left_the_catalogue_is_answered_with_a_deterministic_no_match(
        ConversationResponseKind kind)
    {
        var harness = new RendererHarness();

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(kind) with { ModelId = 10, VariantId = 21 });

        Assert.True(rendered.IsRendered);
        Assert.Contains("مش متاح", rendered.Body, StringComparison.Ordinal);
        Assert.Empty(rendered.DisplayedCandidates);
    }

    [Fact]
    public async Task A_comparison_reloads_every_candidate_and_keeps_the_order_of_the_survivors()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21));
        harness.Details.Publish(ConversationSamples.ActiveModel(11, 25));
        harness.Details.Publish(ConversationSamples.ActiveModel(12, 31));

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductComparison)
                .WithCandidates([(10, 21), (11, 25), (12, 31)]));

        Assert.True(rendered.IsRendered);
        Assert.Empty(rendered.DisplayedCandidates);
        Assert.True(
            rendered.Body!.IndexOf("Dell 10", StringComparison.Ordinal)
            < rendered.Body.IndexOf("Dell 11", StringComparison.Ordinal));
        Assert.True(
            rendered.Body.IndexOf("Dell 11", StringComparison.Ordinal)
            < rendered.Body.IndexOf("Dell 12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_comparison_with_fewer_than_two_current_candidates_is_a_deterministic_no_match()
    {
        var harness = new RendererHarness();
        harness.Details.Publish(ConversationSamples.ActiveModel(10, 21));

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductComparison)
                .WithCandidates([(10, 21), (11, 25)]));

        Assert.True(rendered.IsRendered);
        Assert.Contains("مش متاح", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_business_question_is_answered_with_the_current_stored_arabic_value()
    {
        var harness = new RendererHarness();
        harness.BusinessInfo.Publish(BusinessInfoKeyNames.WorkingHours, "من ١٠ صباحاً لـ ١٠ مساءً");

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.BusinessInfo) with
            {
                StorefrontKey = BusinessInfoKeyNames.WorkingHours,
            });

        Assert.Equal("من ١٠ صباحاً لـ ١٠ مساءً", rendered.Body);
        Assert.Equal([BusinessInfoKeyNames.WorkingHours], harness.BusinessInfo.RequestedKeys);
    }

    [Fact]
    public async Task A_business_question_with_no_active_stored_value_is_answered_safely()
    {
        var harness = new RendererHarness();

        var rendered = await harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.BusinessInfo) with
            {
                StorefrontKey = BusinessInfoKeyNames.Address,
            });

        Assert.True(rendered.IsRendered);
        Assert.Contains("مش متاحة", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_reply_without_an_effective_query_throws_instead_of_disappearing()
    {
        var harness = new RendererHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults)));
    }

    [Fact]
    public async Task A_product_reply_that_identifies_no_product_throws_instead_of_disappearing()
    {
        var harness = new RendererHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.Price)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotAnApprovedKey")]
    public async Task A_business_reply_without_an_approved_key_throws_instead_of_disappearing(string? key)
    {
        var harness = new RendererHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.BusinessInfo) with { StorefrontKey = key }));
    }

    [Fact]
    public async Task A_comparison_of_fewer_than_two_candidates_throws_instead_of_disappearing()
    {
        var harness = new RendererHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductComparison).WithCandidates([(10, 21)])));
    }

    [Fact]
    public async Task An_unavailable_catalogue_is_never_answered_with_an_invented_fact()
    {
        var harness = new RendererHarness();
        harness.Search.OnSearch = _ => throw new InvalidOperationException("The catalogue is unavailable.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.ProductSearchResults)
                .WithSearchQuery(new ProductSearchQuery { Brand = "Dell" })));
    }

    [Fact]
    public async Task An_unavailable_business_info_read_is_never_answered_with_an_invented_fact()
    {
        var harness = new RendererHarness();
        harness.BusinessInfo.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Renderer.RenderAsync(
            harness.Intent(ConversationResponseKind.BusinessInfo) with
            {
                StorefrontKey = BusinessInfoKeyNames.Address,
            }));
    }

    /// <summary>The renderer with its three module contracts replaced by deterministic test data.</summary>
    private sealed class RendererHarness
    {
        internal FakeCatalogSearch Search { get; } = new();

        internal FakeCatalogProductDetails Details { get; } = new();

        internal FakeStorefrontBusinessInfo BusinessInfo { get; } = new();

        internal DeterministicConversationRenderer Renderer { get; }

        internal RendererHarness() =>
            Renderer = new DeterministicConversationRenderer(Search, Details, BusinessInfo);

        internal ConversationResponseIntent Intent(ConversationResponseKind kind) =>
            new()
            {
                Kind = kind,
                ConversationId = 7,
                CustomerExternalId = "20100000001",
            };

        internal ProductRecommendation Recommendation(long modelId, long variantId, decimal price = 2500m) =>
            ConversationSamples.Recommendation(modelId, variantId, price);
    }
}
