using System.Globalization;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The intent routing of docs/TECHNICAL.md section 9. Every branch is deterministic: it may read
/// current facts from Catalog, it may write identifiers, filters and references into the UX state, and
/// it may ask for a clarification, but it never invents a commercial fact and it never guesses which
/// product a customer meant.
/// </summary>
internal sealed class ConversationIntentRouter(
    ICatalogSearch catalogSearch,
    ICatalogProductDetails catalogDetails)
{
    private const string ProductSearchIntent = nameof(NluIntent.ProductSearch);

    /// <summary>Routes one analysed turn. A failed analysis never reaches a commercial module.</summary>
    internal async Task<ConversationRoute> RouteAsync(
        long conversationId,
        string customerExternalId,
        string? body,
        NluAnalysisResult analysis,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        if (analysis.Interpretation is not { } interpretation)
        {
            return FailedAnalysis(conversationId, customerExternalId, analysis.Status, state);
        }

        return interpretation.Intent switch
        {
            NluIntent.Greeting => new ConversationRoute(
                Reply(conversationId, customerExternalId, ConversationResponseKind.Greeting),
                state),
            NluIntent.OutOfScope => new ConversationRoute(
                Reply(conversationId, customerExternalId, ConversationResponseKind.OutOfScope),
                state),
            NluIntent.HumanHandoff => new ConversationRoute(
                Reply(conversationId, customerExternalId, ConversationResponseKind.HumanHandoff),
                state with { LastIntent = nameof(NluIntent.HumanHandoff) }),
            NluIntent.ProductSearch => await SearchAsync(
                conversationId,
                customerExternalId,
                interpretation,
                state,
                cancellationToken),
            NluIntent.ProductDetails => await SingleProductAsync(
                conversationId,
                customerExternalId,
                interpretation,
                state,
                ConversationResponseKind.ProductDetails,
                cancellationToken),
            NluIntent.AvailabilityCheck => await SingleProductAsync(
                conversationId,
                customerExternalId,
                interpretation,
                state,
                ConversationResponseKind.Availability,
                cancellationToken),
            NluIntent.PriceCheck => await SingleProductAsync(
                conversationId,
                customerExternalId,
                interpretation,
                state,
                ConversationResponseKind.Price,
                cancellationToken),
            NluIntent.ProductComparison => Compare(conversationId, customerExternalId, interpretation, state),
            NluIntent.BusinessInfo => BusinessInfo(conversationId, customerExternalId, body, state),
            _ => new ConversationRoute(
                Reply(
                    conversationId,
                    customerExternalId,
                    ConversationResponseKind.UnsupportedMedia,
                    ConversationReasonCodes.InvalidModelOutput),
                state),
        };
    }

    /// <summary>
    /// A search keeps identifiers, the display order and the stated filters. An empty result set is a
    /// deterministic no-match against the stated filters, never a fallback to something above a hard
    /// ceiling or a silently relaxed filter.
    /// </summary>
    private async Task<ConversationRoute> SearchAsync(
        long conversationId,
        string customerExternalId,
        NluInterpretation interpretation,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        var (query, reasonCode) = BuildQuery(interpretation);

        if (query is null)
        {
            return Clarify(conversationId, customerExternalId, state, reasonCode!);
        }

        var results = await catalogSearch.SearchAsync(query, cancellationToken);

        var next = state with
        {
            Shortlist = ConversationStateDocument.BuildShortlist(
                results.Select(result => (result.ModelId, result.VariantId))),
            LastModelId = results.Count == 0 ? null : results[0].ModelId,
            LastVariantId = results.Count == 0 ? null : results[0].VariantId,
            LastIntent = ProductSearchIntent,
            LastFilters = BuildFilters(interpretation),
        };

        if (results.Count == 0)
        {
            return new ConversationRoute(
                Reply(
                    conversationId,
                    customerExternalId,
                    ConversationResponseKind.NoMatch,
                    ConversationReasonCodes.NoMatchUnderFilters),
                next);
        }

        return new ConversationRoute(
            Reply(conversationId, customerExternalId, ConversationResponseKind.ProductSearchResults) with
            {
                ModelIds = [.. results.Select(result => result.ModelId)],
                VariantIds = [.. results.Select(result => result.VariantId)],
            },
            next);
    }

    /// <summary>
    /// One identified product: an exact model code, an allowlisted reference, or the conversation's
    /// current reference. The current facts are always read again from Catalog, so no stale price,
    /// quantity or active state can be answered from the stored state.
    /// </summary>
    private async Task<ConversationRoute> SingleProductAsync(
        long conversationId,
        string customerExternalId,
        NluInterpretation interpretation,
        ConversationStateDocument state,
        ConversationResponseKind kind,
        CancellationToken cancellationToken)
    {
        var (modelId, variantId, reasonCode) = await ResolveProductAsync(interpretation, state, cancellationToken);

        // An exact model code the catalogue does not currently hold is a deterministic no-match: the
        // customer named one specific product. A reference that could not be resolved is different —
        // there the application does not know which product was meant, so it asks.
        if (reasonCode == ConversationReasonCodes.ModelCodeNotAvailable)
        {
            return NoMatch(
                conversationId,
                customerExternalId,
                state,
                interpretation,
                ConversationReasonCodes.ModelCodeNotAvailable);
        }

        if (reasonCode is not null)
        {
            return Clarify(conversationId, customerExternalId, state, reasonCode);
        }

        var facts = await catalogDetails.GetVariantFactsAsync(variantId!.Value, cancellationToken);

        if (facts is null)
        {
            return NoMatch(
                conversationId,
                customerExternalId,
                state,
                interpretation,
                ConversationReasonCodes.ProductNoLongerAvailable);
        }

        return new ConversationRoute(
            Reply(conversationId, customerExternalId, kind) with { ModelId = modelId, VariantId = variantId },
            state with
            {
                LastModelId = modelId,
                LastVariantId = variantId,
                LastIntent = interpretation.Intent.ToString(),
            });
    }

    /// <summary>
    /// A comparison covers the current shortlist in display order. It needs two candidates, and a
    /// reference that cannot be resolved is a clarification rather than a comparison of the wrong items.
    /// </summary>
    private static ConversationRoute Compare(
        long conversationId,
        string customerExternalId,
        NluInterpretation interpretation,
        ConversationStateDocument state)
    {
        if (!string.IsNullOrWhiteSpace(interpretation.Reference))
        {
            var resolution = ConversationReferences.Resolve(interpretation.Reference, state);

            if (!resolution.IsResolved)
            {
                return Clarify(conversationId, customerExternalId, state, resolution.ReasonCode!);
            }
        }

        var shortlist = state.Shortlist.OrderBy(entry => entry.Position).ToList();

        if (shortlist.Count < 2)
        {
            return Clarify(
                conversationId,
                customerExternalId,
                state,
                ConversationReasonCodes.ComparisonNeedsTwoCandidates);
        }

        return new ConversationRoute(
            Reply(conversationId, customerExternalId, ConversationResponseKind.ProductComparison) with
            {
                ModelIds = [.. shortlist.Select(entry => entry.ModelId)],
                VariantIds = [.. shortlist.Select(entry => entry.VariantId)],
            },
            state with { LastIntent = nameof(NluIntent.ProductComparison) });
    }

    /// <summary>
    /// A business-info question resolves to an approved Storefront key through the deterministic
    /// allowlist. The stored answer is deliberately not read here: the renderer reads the current
    /// value, and the UX state never holds one.
    /// </summary>
    private static ConversationRoute BusinessInfo(
        long conversationId,
        string customerExternalId,
        string? body,
        ConversationStateDocument state)
    {
        var key = BusinessInfoScope.ResolveKey(body);

        return key is null
            ? Clarify(
                conversationId,
                customerExternalId,
                state,
                ConversationReasonCodes.BusinessInfoKeyNotResolved)
            : new ConversationRoute(
                Reply(conversationId, customerExternalId, ConversationResponseKind.BusinessInfo) with
                {
                    StorefrontKey = key,
                },
                state with { LastIntent = nameof(NluIntent.BusinessInfo) });
    }

    /// <summary>
    /// Maps one structured interpretation to the catalogue query. A hard budget stays exactly the
    /// ceiling the customer stated, and a stated resolution that cannot be read is a clarification
    /// instead of a silently dropped filter.
    /// </summary>
    private static (ProductSearchQuery? Query, string? ReasonCode) BuildQuery(NluInterpretation interpretation)
    {
        var (width, height, resolutionReason) = ParseResolution(interpretation.Resolution);

        if (resolutionReason is not null)
        {
            return (null, resolutionReason);
        }

        return (new ProductSearchQuery
        {
            ModelCode = interpretation.ModelCode,
            Brand = interpretation.Brand,
            SizeInches = interpretation.SizeInches,
            PanelType = interpretation.Panel,
            MinResolutionWidth = width,
            MinResolutionHeight = height,
            MinRefreshRate = interpretation.MinRefreshRate,
            RequiredPorts = interpretation.RequiredPorts,
            Grades = interpretation.Grades,
            Budget = BuildBudget(interpretation),
            UseCase = interpretation.UseCase,
        }, null);
    }

    /// <summary>
    /// The budget shapes of docs/TECHNICAL.md section 11. The hard ceiling is carried through
    /// unchanged; no path here may raise it.
    /// </summary>
    private static ProductBudget? BuildBudget(NluInterpretation interpretation) => interpretation.BudgetType switch
    {
        NluBudgetType.Soft when interpretation.BudgetTarget is { } target => ProductBudget.Soft(target),
        NluBudgetType.Hard when interpretation.BudgetTarget is { } ceiling => ProductBudget.Hard(ceiling),
        NluBudgetType.Range when interpretation.BudgetMin is { } min && interpretation.BudgetMax is { } max =>
            ProductBudget.Range(min, max),
        _ => null,
    };

    /// <summary>The customer's stated filters, which are context for a later turn and not a fact.</summary>
    private static ConversationStateFilters BuildFilters(NluInterpretation interpretation) => new()
    {
        Brand = interpretation.Brand,
        ModelCode = interpretation.ModelCode,
        SizeInches = interpretation.SizeInches,
        Panel = interpretation.Panel,
        Resolution = interpretation.Resolution,
        MinRefreshRate = interpretation.MinRefreshRate,
        RequiredPorts = interpretation.RequiredPorts,
        Grades = interpretation.Grades,
        BudgetType = interpretation.BudgetType switch
        {
            NluBudgetType.Soft => BudgetType.Soft,
            NluBudgetType.Hard => BudgetType.Hard,
            NluBudgetType.Range => BudgetType.Range,
            _ => BudgetType.None,
        },
        BudgetTarget = interpretation.BudgetTarget,
        BudgetMin = interpretation.BudgetMin,
        BudgetMax = interpretation.BudgetMax,
        UseCase = interpretation.UseCase,
    };

    private async Task<(long? ModelId, long? VariantId, string? ReasonCode)> ResolveProductAsync(
        NluInterpretation interpretation,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(interpretation.ModelCode))
        {
            var found = await catalogSearch.FindByModelCodeAsync(interpretation.ModelCode, cancellationToken);

            return found is null
                ? (null, null, ConversationReasonCodes.ModelCodeNotAvailable)
                : (found.ModelId, found.VariantId, null);
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Reference))
        {
            var resolution = ConversationReferences.Resolve(interpretation.Reference, state);

            return resolution.IsResolved
                ? (resolution.ModelId, resolution.VariantId, null)
                : (null, null, resolution.ReasonCode);
        }

        // An unqualified follow-up means the product the conversation currently references, which is
        // never the first position of the shortlist unless that is what was referenced last.
        return state.LastModelId is { } modelId && state.LastVariantId is { } variantId
            ? (modelId, variantId, null)
            : (null, null, ConversationReferenceReasons.CurrentReferenceMissing);
    }

    private static (int? Width, int? Height, string? ReasonCode) ParseResolution(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return (null, null, null);
        }

        var parts = resolution
            .Replace('\u00D7', 'x')
            .Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            && width > 0
            && height > 0)
        {
            return (width, height, null);
        }

        return (null, null, ConversationReasonCodes.ResolutionNotUnderstood);
    }

    private static ConversationRoute FailedAnalysis(
        long conversationId,
        string customerExternalId,
        NluAnalysisStatus status,
        ConversationStateDocument state) =>
        status == NluAnalysisStatus.InvalidModelOutput
            ? Clarify(conversationId, customerExternalId, state, ConversationReasonCodes.InvalidModelOutput)
            : new ConversationRoute(
                Reply(
                    conversationId,
                    customerExternalId,
                    ConversationResponseKind.AiUnavailable,
                    ConversationReasonCodes.AiUnavailable),
                state);

    private static ConversationRoute Clarify(
        long conversationId,
        string customerExternalId,
        ConversationStateDocument state,
        string reasonCode) =>
        new(Reply(conversationId, customerExternalId, ConversationResponseKind.Clarification, reasonCode), state);

    private static ConversationRoute NoMatch(
        long conversationId,
        string customerExternalId,
        ConversationStateDocument state,
        NluInterpretation interpretation,
        string reasonCode) =>
        new(
            Reply(conversationId, customerExternalId, ConversationResponseKind.NoMatch, reasonCode),
            state with { LastIntent = interpretation.Intent.ToString() });

    private static ConversationResponseIntent Reply(
        long conversationId,
        string customerExternalId,
        ConversationResponseKind kind,
        string? reasonCode = null) =>
        new()
        {
            Kind = kind,
            ConversationId = conversationId,
            CustomerExternalId = customerExternalId,
            ReasonCode = reasonCode,
        };
}
