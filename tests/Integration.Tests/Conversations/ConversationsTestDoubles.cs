using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

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

        var search = new StubCatalogSearch(searchResults ?? [], knownModelCode);
        var nlu = new StubAiNluClient(analysis);
        var renderer = new StubConversationRenderer(details, search, renderedBody);

        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<IAiNluClient>(nlu);
        services.AddSingleton<ICatalogSearch>(search);
        services.AddSingleton<ICatalogProductDetails>(details);

        // A test that wants the fail-closed host of Issue #11 passes false and binds that renderer itself;
        // leaving the stub out lets the production deterministic renderer of the module answer instead.
        if (includeRenderer)
        {
            services.AddSingleton<IConversationRenderer>(renderer);
        }

        return new ConversationDoubles(nlu, details, renderer, search);
    }

    /// <summary>
    /// Binds the fail-closed renderer the host ran before Issue #12, so a test can assert the behaviour of
    /// a host that produces no customer-facing text at all.
    /// </summary>
    public static void AddFailClosedRenderer(this IServiceCollection services) =>
        services.AddSingleton<IConversationRenderer, FailClosedConversationRenderer>();

    /// <summary>
    /// Replaces the Messaging Outbox with one whose first durable enqueue fails, the way a lost
    /// connection or a crashed worker would, so a test can drive the documented "the attempt failed,
    /// the Inbox retries it" path with the real queue behind the retry.
    /// </summary>
    public static void AddFirstAttemptFailingOutbox(this IServiceCollection services)
    {
        services.AddSingleton<EnqueueAttemptCounter>();
        services.AddScoped<OutboundMessageQueue>();
        services.AddScoped<IOutboundMessageQueue>(provider => new FirstAttemptFailingOutbox(
            provider.GetRequiredService<OutboundMessageQueue>(),
            provider.GetRequiredService<EnqueueAttemptCounter>()));
    }
}

/// <summary>Counts the durable enqueues of one test host across every scope of that test.</summary>
internal sealed class EnqueueAttemptCounter
{
    private int attempts;

    public int Next() => Interlocked.Increment(ref attempts);
}

/// <summary>The real durable Outbox, with the first enqueue of a test deliberately failing.</summary>
internal sealed class FirstAttemptFailingOutbox(
    OutboundMessageQueue inner,
    EnqueueAttemptCounter counter) : IOutboundMessageQueue
{
    public Task<OutboundAcceptance?> FindByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default) =>
        inner.FindByCorrelationAsync(correlationId, cancellationToken);

    public async Task<OutboundAcceptance> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (counter.Next() == 1)
        {
            throw new InvalidOperationException("The durable Outbox is unavailable for this attempt.");
        }

        return await inner.EnqueueAsync(request, cancellationToken);
    }
}

/// <summary>The deterministic seams one orchestration test can inspect after a turn.</summary>
internal sealed record ConversationDoubles(
    StubAiNluClient Nlu,
    StubCatalogProductDetails Details,
    StubConversationRenderer Renderer,
    StubCatalogSearch Search);

internal sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset now = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class StubAiNluClient(NluAnalysisResult analysis) : IAiNluClient
{
    private TaskCompletionSource? gate;
    private TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    /// <summary>What the next call answers, so a test can change the customer's next message.</summary>
    public NluAnalysisResult Analysis { get; set; } = analysis;

    /// <summary>
    /// When set, every analysis waits on this gate, so a test can interleave an operator action between
    /// the interpretation of a message and the reply that would follow it. Setting it also arms
    /// <see cref="Entered"/> for the next analysis.
    /// </summary>
    public TaskCompletionSource? Gate
    {
        get => gate;

        set
        {
            gate = value;
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Completes once the analysis of the currently gated turn has really started.</summary>
    public Task Entered => entered.Task;

    public async Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken)
    {
        CallCount++;
        entered.TrySetResult();

        if (gate is { } waiting)
        {
            await waiting.Task.WaitAsync(cancellationToken);
        }

        return Analysis;
    }
}

internal sealed class StubCatalogSearch(
    IReadOnlyList<ProductRecommendation> results,
    string? knownModelCode) : ICatalogSearch
{
    /// <summary>The results of the next search, so a test can change what the catalogue offers.</summary>
    public IReadOnlyList<ProductRecommendation> Results { get; set; } = results;

    public Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default) => Task.FromResult(Results);

    public Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            knownModelCode is not null && string.Equals(knownModelCode, modelCode, StringComparison.Ordinal)
                ? Results.Count == 0 ? null : Results[0]
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
/// renderer uses; for a search reply it re-runs the effective query and returns the current results as the
/// list it displayed, which is what makes a displayed list authoritative instead of tentative.
/// </summary>
internal sealed class StubConversationRenderer(
    ICatalogProductDetails details,
    ICatalogSearch search,
    string? body) : IConversationRenderer
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

        if (intent.Kind == ConversationResponseKind.ProductSearchResults)
        {
            var query = intent.SearchQuery ?? throw new InvalidOperationException(
                "A search reply of this stub needs the effective query of the turn.");
            var results = await search.SearchAsync(query, cancellationToken);

            return ConversationRenderResult.Rendered(
                string.Join('\n', lines),
                results.Select(result => new ConversationDisplayedCandidate(result.ModelId, result.VariantId)));
        }

        return ConversationRenderResult.Rendered(string.Join('\n', lines));
    }
}
