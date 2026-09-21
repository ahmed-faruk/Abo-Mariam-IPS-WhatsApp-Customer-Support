using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>A clock a test advances by hand, so window and expiry decisions are deterministic.</summary>
internal sealed class TestClock(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset now = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now = now.Add(delta);
}

/// <summary>The Conversations persistence of one turn, kept in memory so orchestration is testable.</summary>
internal sealed class FakeConversationTurnStore : IConversationTurnStore
{
    public long CustomerId { get; set; } = 1;

    public long ConversationId { get; set; } = 7;

    public string Mode { get; set; } = ConversationModes.Ai;

    public DateTime? WindowExpiresAt { get; set; }

    public ConversationStateDocument State { get; set; } = ConversationStateDocument.Empty;

    public string? OpenedCustomerExternalId { get; private set; }

    public long? OpenedKnownConversationId { get; private set; }

    public DateTime? AcceptedProviderTimestamp { get; private set; }

    public DateTime? AcceptedAt { get; private set; }

    public DateTime? LastOutboundRecordedAt { get; private set; }

    public int CommitCount { get; private set; }

    public int StateWriteCount { get; private set; }

    public int ModeChangeCount { get; private set; }

    public int OutboundRecordCount { get; private set; }

    public Task<ConversationTurnContext> OpenTurnAsync(
        string customerExternalId,
        long? knownConversationId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        OpenedCustomerExternalId = customerExternalId;
        OpenedKnownConversationId = knownConversationId;

        return Task.FromResult(new ConversationTurnContext
        {
            CustomerId = CustomerId,
            ConversationId = ConversationId,
            Mode = Mode,
            WindowExpiresAt = WindowExpiresAt,
        });
    }

    public Task<ConversationStateDocument> LoadStateAsync(
        long conversationId,
        DateTime utcNow,
        CancellationToken cancellationToken) => Task.FromResult(State);

    public Task AcceptInboundAsync(
        ConversationTurnContext context,
        DateTime providerTimestampUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        AcceptedProviderTimestamp = providerTimestampUtc;
        AcceptedAt = utcNow;
        context.WindowExpiresAt = ConversationWindowPolicy.Refresh(providerTimestampUtc);
        WindowExpiresAt = context.WindowExpiresAt;

        return Task.CompletedTask;
    }

    public Task SaveStateAsync(
        ConversationTurnContext context,
        ConversationStateDocument state,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        State = state;
        StateWriteCount++;

        return Task.CompletedTask;
    }

    public Task SetModeAsync(
        ConversationTurnContext context,
        string mode,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        Mode = mode;
        context.Mode = mode;
        ModeChangeCount++;

        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitCount++;

        return Task.CompletedTask;
    }

    public Task RecordOutboundAsync(long conversationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        LastOutboundRecordedAt = utcNow;
        OutboundRecordCount++;

        return Task.CompletedTask;
    }
}

/// <summary>A renderer that records the intents it was asked for and returns a fixed body.</summary>
internal sealed class FakeConversationRenderer : IConversationRenderer
{
    public List<ConversationResponseIntent> Intents { get; } = [];

    public string? Body { get; set; } = "the rendered reply";

    public Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);

        return Task.FromResult(
            Body is null ? ConversationRenderResult.NotRendered() : ConversationRenderResult.Rendered(Body));
    }
}

/// <summary>Records every durable outbound intent the orchestration asks for.</summary>
internal sealed class RecordingOutboundMessageQueue : IOutboundMessageQueue
{
    public List<OutboundMessageRequest> Requests { get; } = [];

    public long NextMessageId { get; set; } = 501;

    public bool Fails { get; set; }

    public Task<long> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        return Fails
            ? Task.FromException<long>(new InvalidOperationException("The durable Outbox refused the intent."))
            : Task.FromResult(NextMessageId);
    }
}

/// <summary>An NLU client that answers with whatever the test decided.</summary>
internal sealed class FakeAiNluClient : IAiNluClient
{
    public int CallCount { get; private set; }

    public List<string> Messages { get; } = [];

    public Func<string, NluAnalysisResult> Answer { get; set; } =
        _ => NluAnalysisResult.AiUnavailable();

    public Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Messages.Add(message);

        return Task.FromResult(Answer(message));
    }
}

/// <summary>The Catalog search contract, answering from fixed test data.</summary>
internal sealed class FakeCatalogSearch : ICatalogSearch
{
    public List<ProductSearchQuery> Queries { get; } = [];

    public List<string> LookedUpModelCodes { get; } = [];

    public IReadOnlyList<ProductRecommendation> Results { get; set; } = [];

    public ProductRecommendation? ModelCodeResult { get; set; }

    public Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);

        return Task.FromResult(Results);
    }

    public Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default)
    {
        LookedUpModelCodes.Add(modelCode);

        return Task.FromResult(ModelCodeResult);
    }
}

/// <summary>The Catalog current-facts contract, answering from fixed test data.</summary>
internal sealed class FakeCatalogProductDetails : ICatalogProductDetails
{
    public List<long> RequestedVariantIds { get; } = [];

    public Dictionary<long, ProductRecommendation> Variants { get; } = [];

    public Task<ProductDetails?> GetDetailsAsync(
        long productModelId,
        CancellationToken cancellationToken = default) => Task.FromResult<ProductDetails?>(null);

    public Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        RequestedVariantIds.Add(productVariantId);

        return Task.FromResult(Variants.TryGetValue(productVariantId, out var variant) ? variant : null);
    }
}

/// <summary>Independent expected values for the Conversations tests.</summary>
internal static class ConversationSamples
{
    public static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    public static InboundTurn Text(
        string? body = "عندك ديل 24؟",
        string providerMessageId = "wamid.turn-1",
        string customerExternalId = "20100000001",
        DateTime? providerTimestamp = null,
        string messageType = "text",
        long? conversationId = null) =>
        new(
            providerMessageId,
            customerExternalId,
            messageType,
            providerTimestamp ?? Now,
            body,
            conversationId);

    public static NluInterpretation Interpretation(
        NluIntent intent,
        string? reference = null,
        string? modelCode = null,
        string? brand = null,
        decimal? sizeInches = null,
        string? panel = null,
        string? resolution = null,
        int? minRefreshRate = null,
        IReadOnlyList<string>? requiredPorts = null,
        IReadOnlyList<string>? grades = null,
        NluBudgetType budgetType = NluBudgetType.None,
        decimal? budgetTarget = null,
        decimal? budgetMin = null,
        decimal? budgetMax = null,
        string? useCase = null) =>
        new()
        {
            Intent = intent,
            Reference = reference,
            ModelCode = modelCode,
            Brand = brand,
            SizeInches = sizeInches,
            Panel = panel,
            Resolution = resolution,
            MinRefreshRate = minRefreshRate,
            RequiredPorts = requiredPorts ?? [],
            Grades = grades ?? [],
            BudgetType = budgetType,
            BudgetTarget = budgetTarget,
            BudgetMin = budgetMin,
            BudgetMax = budgetMax,
            UseCase = useCase,
        };

    public static ProductRecommendation Recommendation(long modelId, long variantId, decimal price = 2500m) =>
        new()
        {
            ModelId = modelId,
            VariantId = variantId,
            ModelCode = $"M{modelId}",
            Brand = "Dell",
            Model = "P",
            DisplayName = $"Dell {modelId}",
            Price = price,
            Quantity = 3,
            IsAvailable = true,
        };
}
