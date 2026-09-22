using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The current-fact revalidation of docs/TECHNICAL.md section 12 over real PostgreSQL: the deterministic
/// renderer re-runs the effective search and re-reads the current facts immediately before the reply is
/// stored, so a price, a quantity, an active state or a business answer that changed while the turn was
/// being prepared is never quoted from the earlier value, and a product the customer was never shown never
/// becomes reference-resolvable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationRendererRevalidationTests(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private const string Customer = "20100005001";

    private string connectionString = string.Empty;

    private DatabaseCatalogReader catalog = null!;

    private int seededModels;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        catalog = new DatabaseCatalogReader(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PriceCheck_ReloadsCurrentPrice()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        StubAiNluClient nlu = null!;
        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => nlu = services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"))));

        var search = await ProcessAsync(host, "wamid.price-1");

        Assert.Contains("2400", await OutboxBodyAsync(search.OutboxMessageId), StringComparison.Ordinal);

        await SetPriceAsync(variantId, 2999m);
        nlu.Analysis = NluAnalysisResult.Success(Interpretation(NluIntent.PriceCheck, reference: "first"));

        var price = await ProcessAsync(host, "wamid.price-2");
        var body = await OutboxBodyAsync(price.OutboxMessageId);

        // The follow-up resolved the product the displayed list really put first, and quoted the price as
        // PostgreSQL holds it now instead of the price of the earlier turn.
        Assert.Contains("2999", body, StringComparison.Ordinal);
        Assert.DoesNotContain("2400", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HardBudget_NeverReturnsPriceAboveCeiling()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(
                    NluIntent.ProductSearch,
                    brand: "Dell",
                    budgetType: NluBudgetType.Hard,
                    budgetTarget: 2500m)),
                _ => SetPriceAsync(variantId, 2700m)));

        var result = await ProcessAsync(host, "wamid.hard-budget");
        var body = await OutboxBodyAsync(result.OutboxMessageId);

        // A recommendation above the customer's ceiling must not be shown, so the reply says the ceiling is
        // why nothing matched, and no list becomes addressable.
        Assert.DoesNotContain("2700", body, StringComparison.Ordinal);
        Assert.Contains("الحد السعري", body, StringComparison.Ordinal);
        Assert.Equal("0", await ShortlistCountAsync(result.ConversationId));
    }

    [Fact]
    public async Task QuantityZero_IsNeverRecommended()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell")),
                _ => SetQuantityAsync(variantId, 0)));

        var result = await ProcessAsync(host, "wamid.no-stock");

        Assert.DoesNotContain("2400", await OutboxBodyAsync(result.OutboxMessageId), StringComparison.Ordinal);
        Assert.Equal("0", await ShortlistCountAsync(result.ConversationId));
    }

    [Fact]
    public async Task SoftBudget_UsesCatalogConfiguredTolerance()
    {
        var (_, variantId) = await SeedModelAsync(price: 2900m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(
                    NluIntent.ProductSearch,
                    brand: "Dell",
                    budgetType: NluBudgetType.Soft,
                    budgetTarget: 2400m)),
                // The configured tolerance reaches 3000, so 2900 qualifies on the preliminary search. The
                // price then leaves the tolerated range before the reply re-reads the current facts.
                _ => SetPriceAsync(variantId, 3500m)));

        var result = await ProcessAsync(host, "wamid.soft-budget");
        var body = await OutboxBodyAsync(result.OutboxMessageId);

        Assert.DoesNotContain("3500", body, StringComparison.Ordinal);
        Assert.DoesNotContain("الحد السعري", body, StringComparison.Ordinal);
        Assert.Contains("ملقتش", body, StringComparison.Ordinal);
        Assert.Equal("0", await ShortlistCountAsync(result.ConversationId));
    }

    [Fact]
    public async Task An_active_product_whose_current_quantity_is_zero_is_reported_unavailable()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        StubAiNluClient nlu = null!;
        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => nlu = services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"))));

        var search = await ProcessAsync(host, "wamid.sold-out-1");

        Assert.Equal("1", await ShortlistCountAsync(search.ConversationId));

        // The product the customer was shown sells out, and then they ask about it again.
        await SetQuantityAsync(variantId, 0);
        nlu.Analysis = NluAnalysisResult.Success(
            Interpretation(NluIntent.AvailabilityCheck, reference: "first"));

        var availability = await ProcessAsync(host, "wamid.sold-out-2");
        var body = await OutboxBodyAsync(availability.OutboxMessageId);

        // An active product with no stock is still a real identity: it is reported as unavailable instead
        // of being treated as a product that does not exist.
        Assert.Contains("غير متوفر", body, StringComparison.Ordinal);
        Assert.Contains("Dell", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variant_that_became_inactive_is_never_recommended()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell")),
                _ => SetVariantActiveAsync(variantId, isActive: false)));

        var result = await ProcessAsync(host, "wamid.inactive");

        Assert.DoesNotContain("2400", await OutboxBodyAsync(result.OutboxMessageId), StringComparison.Ordinal);
        Assert.Equal("0", await ShortlistCountAsync(result.ConversationId));
    }

    [Fact]
    public async Task The_displayed_list_is_the_current_results_of_the_effective_query()
    {
        var (firstModelId, _) = await SeedModelAsync(price: 2400m, quantity: 3);
        var (secondModelId, _) = await SeedModelAsync(price: 2600m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"))));

        var result = await ProcessAsync(host, "wamid.displayed");

        // Both active, in-stock Dell screens qualify, so both are displayed and both become addressable in
        // the order the reply shows them.
        Assert.Equal("2", await ShortlistCountAsync(result.ConversationId));
        Assert.Equal($"{firstModelId}", await StateAsync(result.ConversationId, "state_json->>'lastModelId'"));
        Assert.Equal("1", await StateAsync(result.ConversationId, "state_json#>>'{shortlist,0,position}'"));
        Assert.Equal($"{secondModelId}", await StateAsync(result.ConversationId, "state_json#>>'{shortlist,1,modelId}'"));

        // The payload stored next to the immutable reply is exactly that list.
        var metadata = await MetadataAsync(result.OutboxMessageId);

        Assert.Contains($"\"m\":{firstModelId}", metadata, StringComparison.Ordinal);
        Assert.Contains($"\"m\":{secondModelId}", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_storefront_answer_is_read_immediately_before_the_reply()
    {
        await SeedBusinessInfoAsync(BusinessInfoKeyNames.WorkingHours, "من ١٠ صباحاً لـ ٦ مساءً");

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.BusinessInfo))));

        var first = await ProcessAsync(host, "wamid.faq-1", body: "مواعيدكم إيه؟");

        Assert.Equal("من ١٠ صباحاً لـ ٦ مساءً", await OutboxBodyAsync(first.OutboxMessageId));

        await SetBusinessInfoAnswerAsync(BusinessInfoKeyNames.WorkingHours, "من ١٠ صباحاً لـ ١٠ مساءً");

        var second = await ProcessAsync(host, "wamid.faq-2", body: "مواعيدكم إيه؟");

        Assert.Equal("من ١٠ صباحاً لـ ١٠ مساءً", await OutboxBodyAsync(second.OutboxMessageId));
    }

    [Fact]
    public async Task A_replayed_turn_reuses_the_immutable_reply_instead_of_rendering_it_again()
    {
        var (_, variantId) = await SeedModelAsync(price: 2400m, quantity: 3);

        await using var host = ConversationRendererHost.Start(
            connectionString,
            services => services.AddRealRenderer(
                NluAnalysisResult.Success(Interpretation(NluIntent.ProductSearch, brand: "Dell"))));

        var first = await ProcessAsync(host, "wamid.accept-1");
        var body = await OutboxBodyAsync(first.OutboxMessageId);

        await SetPriceAsync(variantId, 2700m);

        // Replaying the same inbound turn must reuse the reply that is already durable: the customer was
        // sent that text, so the newer price may not be rendered as if it had been.
        var retried = await ProcessAsync(host, "wamid.accept-1");

        Assert.Equal(first.OutboxMessageId, retried.OutboxMessageId);
        Assert.Equal("1", await OutboxCountAsync());
        Assert.Equal(body, await OutboxBodyAsync(retried.OutboxMessageId));
        Assert.Equal("1", await ShortlistCountAsync(retried.ConversationId));
    }

    private Task<string> ShortlistCountAsync(long conversationId) =>
        StateAsync(conversationId, "coalesce(jsonb_array_length(state_json->'shortlist'), 0)");

    private Task<string> StateAsync(long conversationId, string expression) =>
        catalog.ScalarAsync(
            $"SELECT {expression} FROM conversations.conversation_state WHERE conversation_id = {conversationId}");

    private Task<string> OutboxCountAsync() =>
        catalog.ScalarAsync("SELECT count(*) FROM messaging.outbox_message");

    private Task<string> OutboxBodyAsync(long? outboxMessageId) =>
        catalog.ScalarAsync($"SELECT body FROM messaging.outbox_message WHERE id = {outboxMessageId}");

    private Task<string> MetadataAsync(long? outboxMessageId) =>
        catalog.ScalarAsync($"SELECT application_metadata FROM messaging.outbox_message WHERE id = {outboxMessageId}");

    private static async Task<ConversationTurnResult> ProcessAsync(
        ConversationRendererHost host,
        string providerMessageId,
        string body = "عندك ديل 24؟")
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>().ProcessAsync(
            new InboundTurn(
                providerMessageId,
                Customer,
                "text",
                ConversationsTestDoubles.Now,
                body));
    }

    private static NluInterpretation Interpretation(
        NluIntent intent,
        string? reference = null,
        string? brand = null,
        NluBudgetType budgetType = NluBudgetType.None,
        decimal? budgetTarget = null) => new()
        {
            Intent = intent,
            Reference = reference,
            Brand = brand,
            RequiredPorts = [],
            Grades = [],
            BudgetType = budgetType,
            BudgetTarget = budgetTarget,
        };

    /// <summary>Seeds one active Dell screen with one active, in-stock variant and returns their new ids.</summary>
    private async Task<(long ModelId, long VariantId)> SeedModelAsync(decimal price, int quantity)
    {
        var modelCode = $"M-DELL-{++seededModels}";
        var modelId = long.Parse(
            await catalog.ScalarAsync(
                "INSERT INTO catalog.product_model "
                + "(model_code, brand, model, display_name, size_inches, panel_type, "
                + "resolution_width, resolution_height, refresh_rate, is_active) "
                + $"VALUES ('{modelCode}', 'Dell', 'P', 'Dell P2419H', 24, 'IPS', 1920, 1080, 60, true) "
                + "RETURNING id"),
            CultureInfo.InvariantCulture);

        await catalog.ExecuteAsync(
            "INSERT INTO catalog.product_model_port (product_model_id, port_type, count) "
            + $"VALUES ({modelId}, 'HDMI', 1)");

        var variantId = long.Parse(
            await catalog.ScalarAsync(
                "INSERT INTO catalog.product_variant "
                + "(product_model_id, sku, grade, selling_price, quantity, warranty_days, is_active) "
                + $"VALUES ({modelId}, 'SKU-{modelId}', 'A', "
                + string.Create(CultureInfo.InvariantCulture, $"{price}, {quantity}, 90, true")
                + ") RETURNING id"),
            CultureInfo.InvariantCulture);

        return (modelId, variantId);
    }

    private Task SetPriceAsync(long variantId, decimal price) =>
        catalog.ExecuteAsync(
            $"UPDATE catalog.product_variant SET selling_price = {price} WHERE id = {variantId}");

    private Task SetQuantityAsync(long variantId, int quantity) =>
        catalog.ExecuteAsync($"UPDATE catalog.product_variant SET quantity = {quantity} WHERE id = {variantId}");

    private Task SetVariantActiveAsync(long variantId, bool isActive) =>
        catalog.ExecuteAsync($"UPDATE catalog.product_variant SET is_active = {isActive} WHERE id = {variantId}");

    private Task SeedBusinessInfoAsync(string key, string answerAr) =>
        catalog.ExecuteAsync(
            "INSERT INTO storefront.business_info (\"key\", answer_ar, is_active) "
            + $"VALUES ('{key}', '{answerAr}', true)");

    private Task SetBusinessInfoAnswerAsync(string key, string answerAr) =>
        catalog.ExecuteAsync(
            $"UPDATE storefront.business_info SET answer_ar = '{answerAr}' WHERE \"key\" = '{key}'");
}
