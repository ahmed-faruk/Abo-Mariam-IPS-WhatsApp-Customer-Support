using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>Registers the deterministic seams one orchestration test substitutes for the real ones.</summary>
internal static class ConversationsTestDoubles
{
    /// <summary>A fixed clock, so window and expiry decisions never depend on the wall clock.</summary>
    public static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Registers the doubles and hands them back, so a test can observe what they were asked.</summary>
    public static ConversationDoubles AddConversationDoubles(
        this IServiceCollection services,
        NluAnalysisResult analysis,
        string? renderedBody = "the deterministic reply",
        IReadOnlyList<ProductRecommendation>? searchResults = null,
        string? knownModelCode = null,
        Action<StubCatalogProductDetails>? publishFacts = null,
        bool includeRenderer = true)
    {
        var details = new StubCatalogProductDetails();
        publishFacts?.Invoke(details);

        var nlu = new StubAiNluClient(analysis);
        var renderer = new StubConversationRenderer(details, renderedBody);

        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<IAiNluClient>(nlu);
        services.AddSingleton<ICatalogSearch>(new StubCatalogSearch(searchResults ?? [], knownModelCode));
        services.AddSingleton<ICatalogProductDetails>(details);

        if (includeRenderer)
        {
            services.AddSingleton<IConversationRenderer>(renderer);
        }

        return new ConversationDoubles(nlu, details, renderer);
    }
}

/// <summary>The deterministic seams one orchestration test can inspect after a turn.</summary>
internal sealed record ConversationDoubles(
    StubAiNluClient Nlu,
    StubCatalogProductDetails Details,
    StubConversationRenderer Renderer);

internal sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset now = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class StubAiNluClient(NluAnalysisResult analysis) : IAiNluClient
{
    public int CallCount { get; private set; }

    /// <summary>What the next call answers, so a test can change the customer's next message.</summary>
    public NluAnalysisResult Analysis { get; set; } = analysis;

    public Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken)
    {
        CallCount++;

        return Task.FromResult(Analysis);
    }
}

internal sealed class StubCatalogSearch(
    IReadOnlyList<ProductRecommendation> results,
    string? knownModelCode) : ICatalogSearch
{
    public Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default) => Task.FromResult(results);

    public Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            knownModelCode is not null && string.Equals(knownModelCode, modelCode, StringComparison.Ordinal)
                ? results.Count == 0 ? null : results[0]
                : null);
}

internal sealed class StubCatalogProductDetails : ICatalogProductDetails
{
    private readonly Dictionary<long, ProductRecommendation> variants = [];

    public IReadOnlyList<long> RequestedVariantIds => [.. requested];

    private readonly List<long> requested = [];

    public void Publish(ProductRecommendation recommendation) => variants[recommendation.VariantId] = recommendation;

    public void Withdraw(long productVariantId) => variants.Remove(productVariantId);

    /// <summary>
    /// The routing revalidation reads the model and its variant rows. A published recommendation stands
    /// for an active model with an active variant, so a withdrawn one stands for a row that is gone.
    /// </summary>
    public Task<ProductDetails?> GetDetailsAsync(
        long productModelId,
        CancellationToken cancellationToken = default)
    {
        var published = variants.Values.Where(variant => variant.ModelId == productModelId).ToList();

        if (published.Count == 0)
        {
            return Task.FromResult<ProductDetails?>(null);
        }

        return Task.FromResult<ProductDetails?>(new ProductDetails
        {
            ModelId = productModelId,
            ModelCode = published[0].ModelCode,
            Brand = published[0].Brand,
            DisplayName = published[0].DisplayName,
            IsActive = true,
            Variants =
            [
                .. published.Select(variant => new ProductVariantDetails
                {
                    VariantId = variant.VariantId,
                    Sku = variant.Sku,
                    Price = variant.Price,
                    Quantity = variant.Quantity,
                    IsActive = true,
                }),
            ],
        });
    }

    public Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        requested.Add(productVariantId);

        return Task.FromResult(variants.TryGetValue(productVariantId, out var published) ? published : null);
    }
}

/// <summary>
/// A deterministic stand-in for the Issue #12 renderer. For a price or availability reply it reads the
/// current facts through the Catalog contract, which is exactly the revalidation boundary the real
/// renderer will use, so the integration suite proves the seam instead of assuming it.
/// </summary>
internal sealed class StubConversationRenderer(ICatalogProductDetails details, string? body) : IConversationRenderer
{
    public List<ConversationResponseIntent> Intents { get; } = [];

    public async Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);

        if (body is null)
        {
            return ConversationRenderResult.NotRendered();
        }

        var lines = new List<string> { body };

        if (intent is { Kind: ConversationResponseKind.Price, VariantId: { } variantId })
        {
            var facts = await details.GetVariantFactsAsync(variantId, cancellationToken);

            // The renderer never reads the price from the intent or from conversation state; it reads
            // the current catalogue value immediately before producing the text.
            lines.Add($"price={facts?.Price.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        }

        return ConversationRenderResult.Rendered(string.Join('\n', lines));
    }
}
