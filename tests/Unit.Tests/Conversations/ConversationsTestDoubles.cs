using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

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
    /// <summary>The order the turn touched its collaborators, so durability ordering is observable.</summary>
    public List<string> Journal { get; init; } = [];

    public long CustomerId { get; set; } = 1;

    public long ConversationId { get; set; } = 7;

    public string Mode { get; set; } = ConversationModes.Ai;

    /// <summary>How many explicit mode decisions the stored conversation has had, as the real row counts them.</summary>
    public long ModeRevision { get; set; }

    public DateTime? WindowExpiresAt { get; set; }

    public ConversationStateDocument State { get; set; } = ConversationStateDocument.Empty;

    /// <summary>
    /// Raw persisted JSON. When a test sets it, the turn reads the stored representation back the way
    /// the real store does, instead of handing the orchestration a document a test already built.
    /// </summary>
    public string? StoredStateJson { get; set; }

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
        Journal.Add("store:open-turn");
        OpenedCustomerExternalId = customerExternalId;
        OpenedKnownConversationId = knownConversationId;

        return Task.FromResult(new ConversationTurnContext
        {
            CustomerId = CustomerId,
            ConversationId = ConversationId,
            Mode = Mode,
            ModeRevision = ModeRevision,
            WindowExpiresAt = WindowExpiresAt,
        });
    }

    public Task<ConversationStateDocument> LoadStateAsync(
        long conversationId,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            StoredStateJson is null ? State : ConversationStateDocument.Deserialize(StoredStateJson));

    public Task<IConversationOperation> BeginFinalOperationAsync(
        long conversationId,
        CancellationToken cancellationToken)
    {
        Journal.Add("store:begin-final-operation");
        OnBeginFinalOperation?.Invoke();

        return Task.FromResult<IConversationOperation>(new FakeConversationOperation(this, Journal));
    }

    /// <summary>
    /// Runs as the turn's final operation begins, so a test can interleave an operator action between the
    /// mode the inbound was accepted with and the mode the final section reads again.
    /// </summary>
    public Action? OnBeginFinalOperation { get; set; }

    public Task AcceptInboundAsync(
        ConversationTurnContext context,
        DateTime providerTimestampUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        Journal.Add("store:accept-inbound");
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
        Journal.Add("store:save-state");
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
        Journal.Add($"store:set-mode:{mode}");

        if (!string.Equals(Mode, mode, StringComparison.Ordinal))
        {
            ModeRevision++;
        }

        Mode = mode;
        context.Mode = mode;
        context.ModeRevision = ModeRevision;
        ModeChangeCount++;

        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Journal.Add("store:commit");
        CommitCount++;

        return Task.CompletedTask;
    }

    public Task RecordOutboundAsync(long conversationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        Journal.Add("store:record-outbound");
        LastOutboundRecordedAt = utcNow;
        OutboundRecordCount++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies one of the operator's own mode decisions the way the real mode control does: a decision that
    /// really changes the mode advances the conversation's mode revision by one, and one that changes
    /// nothing does not.
    /// </summary>
    public void OperatorDecides(string mode)
    {
        Journal.Add($"operator:decides:{mode}");

        if (string.Equals(Mode, mode, StringComparison.Ordinal))
        {
            return;
        }

        Mode = mode;
        ModeRevision++;
    }

    /// <summary>
    /// The serialized final section of one turn. The real operation takes a PostgreSQL lock that orders
    /// it against the operator's mode changes; here the same decision is observable through the journal:
    /// the mode is read again as it is stored now, and the commit releases the section.
    /// </summary>
    private sealed class FakeConversationOperation(
        FakeConversationTurnStore store,
        List<string> journal) : IConversationOperation
    {
        public Task<ConversationModeSnapshot> ReloadModeAsync(CancellationToken cancellationToken)
        {
            journal.Add("store:reload-mode");

            return Task.FromResult(new ConversationModeSnapshot(store.Mode, store.ModeRevision));
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            journal.Add("store:commit-final-operation");
            store.CommitCount++;

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            journal.Add("store:release-final-operation");

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A renderer that records the intents it was asked for and returns a fixed body.</summary>
internal sealed class FakeConversationRenderer : IConversationRenderer
{
    public List<string> Journal { get; init; } = [];

    public List<ConversationResponseIntent> Intents { get; } = [];

    public string? Body { get; set; } = "the rendered reply";

    /// <summary>Throws instead of answering, the way an unavailable catalogue read does.</summary>
    public Func<ConversationResponseIntent, Exception>? Fails { get; set; }

    /// <summary>
    /// The ordered products the reply displays. It is the renderer's own answer, which is the only thing
    /// that may make a list addressable, so a test states it independently of the route.
    /// </summary>
    public Func<ConversationResponseIntent, IReadOnlyList<(long ModelId, long VariantId)>> DisplayedCandidates
    { get; set; } = _ => [];

    public Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        Journal.Add("renderer:render");
        Intents.Add(intent);

        if (Fails is { } failure)
        {
            return Task.FromException<ConversationRenderResult>(failure(intent));
        }

        if (Body is null)
        {
            return Task.FromResult(ConversationRenderResult.NotRendered());
        }

        return Task.FromResult(ConversationRenderResult.Rendered(
            Body,
            DisplayedCandidates(intent).Select(
                pair => new ConversationDisplayedCandidate(pair.ModelId, pair.VariantId))));
    }
}

/// <summary>Records every durable outbound intent the orchestration asks for.</summary>
internal sealed class RecordingOutboundMessageQueue : IOutboundMessageQueue
{
    private readonly Dictionary<string, OutboundAcceptance> accepted = [];

    public List<string> Journal { get; init; } = [];

    public List<OutboundMessageRequest> Requests { get; } = [];

    public long NextMessageId { get; set; } = 501;

    /// <summary>The conversation this queue stores for, which is the one its requests name.</summary>
    public long ConversationId { get; set; } = 7;

    public bool Fails { get; set; }

    /// <summary>
    /// The durable row another writer already holds the correlation with, so an enqueue of this queue
    /// loses the conflict even though its own read found nothing.
    /// </summary>
    public OutboundAcceptance? ConflictingAcceptance { get; set; }

    /// <summary>
    /// Stores a durable reply the way an earlier attempt of the same turn would have, so a test can drive
    /// the retry path with an already accepted correlation.
    /// </summary>
    /// <param name="conversationId">
    /// The conversation the row was really accepted for, when a test needs a reply that belongs to a
    /// different conversation than the turn under test.
    /// </param>
    public void PreStore(
        string correlationId,
        string body,
        string? applicationMetadata = null,
        long? conversationId = null) =>
        accepted[correlationId] = new OutboundAcceptance(
            NextMessageId,
            conversationId ?? ConversationId,
            body,
            applicationMetadata,
            IsExisting: true);

    public Task<OutboundAcceptance?> FindByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Journal.Add("outbox:find");

        return Task.FromResult(accepted.TryGetValue(correlationId, out var existing) ? existing : null);
    }

    public Task<OutboundAcceptance> EnqueueAsync(
        OutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        Journal.Add("outbox:enqueue");
        Requests.Add(request);

        if (Fails)
        {
            return Task.FromException<OutboundAcceptance>(
                new InvalidOperationException("The durable Outbox refused the intent."));
        }

        if (ConflictingAcceptance is { } conflict)
        {
            return Task.FromResult(conflict with { IsExisting = true });
        }

        if (accepted.TryGetValue(request.CorrelationId, out var existing))
        {
            // A correlation already has an immutable row, so its stored body and metadata win.
            return Task.FromResult(existing with { IsExisting = true });
        }

        var stored = new OutboundAcceptance(
            NextMessageId++,
            request.ConversationId,
            request.Body,
            request.ApplicationMetadata,
            IsExisting: false);

        accepted[request.CorrelationId] = stored;

        return Task.FromResult(stored);
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

    /// <summary>Answers a specific query, so a test can show what the current catalogue returns now.</summary>
    public Func<ProductSearchQuery, IReadOnlyList<ProductRecommendation>>? OnSearch { get; set; }

    public Task<IReadOnlyList<ProductRecommendation>> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);

        return Task.FromResult(OnSearch?.Invoke(query) ?? Results);
    }

    public Task<ProductRecommendation?> FindByModelCodeAsync(
        string modelCode,
        CancellationToken cancellationToken = default)
    {
        LookedUpModelCodes.Add(modelCode);

        return Task.FromResult(ModelCodeResult);
    }
}

/// <summary>The Storefront business-info contract, answering from fixed test data.</summary>
internal sealed class FakeStorefrontBusinessInfo : IStorefrontBusinessInfo
{
    public Dictionary<string, BusinessInfoValue> Values { get; } = [];

    public List<string> RequestedKeys { get; } = [];

    /// <summary>Throws instead of answering, the way an unavailable Storefront read does.</summary>
    public bool Fails { get; set; }

    /// <summary>Publishes one active stored answer, the way a seeded Storefront row would answer.</summary>
    public void Publish(string key, string answerAr) =>
        Values[key] = new BusinessInfoValue(key, answerAr, null, IsActive: true, ConversationSamples.Now);

    public Task<BusinessInfoValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        RequestedKeys.Add(key);

        return Fails
            ? Task.FromException<BusinessInfoValue?>(new InvalidOperationException("Storefront is unavailable."))
            : Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
    }
}

/// <summary>The Catalog current-facts contract, answering from fixed test data.</summary>
internal sealed class FakeCatalogProductDetails : ICatalogProductDetails
{
    /// <summary>The current catalogue rows, keyed by model id. A model that was not published does not exist.</summary>
    public Dictionary<long, ProductDetails> Models { get; } = [];

    public List<long> RequestedModelIds { get; } = [];

    public List<long> RequestedVariantIds { get; } = [];

    public void Publish(ProductDetails details) => Models[details.ModelId] = details;

    /// <summary>Removes a model from the catalogue, as a retired row would disappear.</summary>
    public void Withdraw(long modelId) => Models.Remove(modelId);

    public Task<ProductDetails?> GetDetailsAsync(
        long productModelId,
        CancellationToken cancellationToken = default)
    {
        RequestedModelIds.Add(productModelId);

        return Task.FromResult(Models.TryGetValue(productModelId, out var details) ? details : null);
    }

    public Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        RequestedVariantIds.Add(productVariantId);

        // Routing revalidates through GetDetailsAsync, because only that shape distinguishes a retired
        // row from an active one that currently has no stock. This shape answers the current facts of one
        // variant, which is what a reply about a single product reads.
        var published = Models.Values
            .SelectMany(details => details.Variants.Select(variant => (Details: details, Variant: variant)))
            .FirstOrDefault(candidate => candidate.Variant.VariantId == productVariantId);

        if (published.Details is null || published.Variant is null)
        {
            return Task.FromResult<ProductRecommendation?>(null);
        }

        return Task.FromResult<ProductRecommendation?>(new ProductRecommendation
        {
            ModelId = published.Details.ModelId,
            ModelCode = published.Details.ModelCode,
            Brand = published.Details.Brand,
            Model = published.Details.Model,
            DisplayName = published.Details.DisplayName,
            SizeInches = published.Details.SizeInches,
            PanelType = published.Details.PanelType,
            ResolutionWidth = published.Details.ResolutionWidth,
            ResolutionHeight = published.Details.ResolutionHeight,
            RefreshRate = published.Details.RefreshRate,
            Tags = published.Details.Tags,
            Ports = [.. published.Details.Ports.Select(port => port.PortType)],
            VariantId = published.Variant.VariantId,
            Sku = published.Variant.Sku,
            Grade = published.Variant.Grade,
            Price = published.Variant.Price,
            Quantity = published.Variant.Quantity,
            WarrantyDays = published.Variant.WarrantyDays,
            WarrantyNotes = published.Variant.WarrantyNotes,
            CosmeticNotes = published.Variant.CosmeticNotes,
            IsAvailable = published.Details.IsActive && published.Variant.IsActive && published.Variant.Quantity > 0,
        });
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

    /// <summary>One catalogue model row with exactly the variants the test states.</summary>
    public static ProductDetails Model(long modelId, bool isActive, params ProductVariantDetails[] variants) =>
        new()
        {
            ModelId = modelId,
            ModelCode = $"M{modelId}",
            Brand = "Dell",
            Model = "P",
            DisplayName = $"Dell {modelId}",
            IsActive = isActive,
            Variants = [.. variants],
        };

    /// <summary>One catalogue variant row, active or retired, with any quantity including zero.</summary>
    public static ProductVariantDetails Variant(long variantId, bool isActive, int quantity) =>
        new()
        {
            VariantId = variantId,
            Sku = $"SKU-{variantId}",
            Price = 2500m,
            Quantity = quantity,
            IsActive = isActive,
        };

    /// <summary>The ordinary case: one active model with one active variant that holds stock.</summary>
    public static ProductDetails ActiveModel(long modelId, long variantId, int quantity = 3) =>
        Model(modelId, isActive: true, Variant(variantId, isActive: true, quantity));
}
