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
                body,
                state,
                ConversationResponseKind.ProductDetails,
                cancellationToken),
            NluIntent.AvailabilityCheck => await SingleProductAsync(
                conversationId,
                customerExternalId,
                interpretation,
                body,
                state,
                ConversationResponseKind.Availability,
                cancellationToken),
            NluIntent.PriceCheck => await SingleProductAsync(
                conversationId,
                customerExternalId,
                interpretation,
                body,
                state,
                ConversationResponseKind.Price,
                cancellationToken),
            NluIntent.ProductComparison => await CompareAsync(
                conversationId,
                customerExternalId,
                interpretation,
                body,
                state,
                cancellationToken),
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
    /// A search keeps identifiers, the display order and the effective filters. A follow-up refines the
    /// search the conversation already holds: this turn's fields override the stored ones and every field
    /// it does not name is retained, so "Dell 24" followed by "IPS with HDMI" searches for all four. An
    /// empty result set is never a fallback to something above a hard ceiling or a silently relaxed
    /// filter: the reply carries the effective query, and the final search immediately before the durable
    /// enqueue is what either displays current products or answers a deterministic no-match.
    /// The effective filters are the customer's own words and are stored as soon as the turn is accepted.
    /// The result list is not: a shortlist becomes addressable only once the reply that showed it is
    /// durably stored, so a search whose answer never reaches the customer - a closed service window, an
    /// unbound renderer, a failed enqueue - cannot leave a list behind that the customer never saw.
    /// </summary>
    private async Task<ConversationRoute> SearchAsync(
        long conversationId,
        string customerExternalId,
        NluInterpretation interpretation,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        var filters = MergeFilters(state.LastFilters, interpretation);
        var (query, reasonCode) = BuildQuery(filters);

        if (query is null)
        {
            return Clarify(conversationId, customerExternalId, state, reasonCode!);
        }

        // The routing-time read of docs/TECHNICAL.md section 9: the search branch really asks the catalogue
        // what its query means now. Its answer is deliberately not what the customer is shown. A product
        // that is out of stock, retired or above a hard ceiling at this moment may be in stock, active and
        // affordable moments later, and the reply would then quote a stale no-match while the customer's
        // request could have been satisfied. What is displayed is decided by the final search the renderer
        // runs immediately before the reply is stored, so only that read is authoritative.
        await catalogSearch.SearchAsync(query, cancellationToken);

        var next = state with
        {
            LastIntent = ProductSearchIntent,
            LastFilters = filters,
        };

        // What the reply displays is decided by the final search the renderer runs immediately before the
        // reply is stored, so this turn carries the effective query and nothing that claims a list was
        // shown. The customer's own filters do travel with the turn, because they are their own words.
        return new ConversationRoute(
            Reply(conversationId, customerExternalId, ConversationResponseKind.ProductSearchResults)
                .WithSearchQuery(query),
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
        string? body,
        ConversationStateDocument state,
        ConversationResponseKind kind,
        CancellationToken cancellationToken)
    {
        var (modelId, variantId, reasonCode) =
            await ResolveProductAsync(interpretation, body, state, cancellationToken);

        // An exact model code the catalogue does not currently hold is a deterministic no-match: the
        // customer named one specific product. A reference that could not be resolved is different —
        // there the application does not know which product was meant, so it asks.
        if (reasonCode == ConversationReasonCodes.ModelCodeNotAvailable)
        {
            // The customer named one exact product and the catalogue does not hold it. The current
            // reference goes with the product it named: an unqualified follow-up such as "سعرها؟" must
            // ask which product is meant instead of silently answering about the previously referenced
            // one. The list the customer was already shown stays available by position.
            return new ConversationRoute(
                Reply(
                    conversationId,
                    customerExternalId,
                    ConversationResponseKind.NoMatch,
                    ConversationReasonCodes.ModelCodeNotAvailable),
                state with
                {
                    LastModelId = null,
                    LastVariantId = null,
                    LastIntent = interpretation.Intent.ToString(),
                });
        }

        if (reasonCode is not null)
        {
            return Clarify(conversationId, customerExternalId, state, reasonCode);
        }

        if (!await IsCurrentlyActiveAsync(modelId!.Value, variantId!.Value, cancellationToken))
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
    /// A comparison covers the current shortlist in display order. It needs two candidates, a reference
    /// that cannot be resolved is a clarification rather than a comparison of the wrong items, and every
    /// candidate is reloaded through the catalogue before it is named: a stored shortlist is identity and
    /// order only, so a candidate that was retired since the search makes the comparison a deterministic
    /// no-match instead of a comparison of stale identifiers.
    /// </summary>
    private async Task<ConversationRoute> CompareAsync(
        long conversationId,
        string customerExternalId,
        NluInterpretation interpretation,
        string? body,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(interpretation.Reference))
        {
            var resolution = ConversationReferences.ResolveWithCustomerText(interpretation.Reference, body, state);

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

        foreach (var entry in shortlist)
        {
            if (!await IsCurrentlyActiveAsync(entry.ModelId, entry.VariantId, cancellationToken))
            {
                return NoMatch(
                    conversationId,
                    customerExternalId,
                    state,
                    interpretation,
                    ConversationReasonCodes.ProductNoLongerAvailable);
            }
        }

        return new ConversationRoute(
            Reply(conversationId, customerExternalId, ConversationResponseKind.ProductComparison)
                .WithCandidates(shortlist.Select(entry => (entry.ModelId, entry.VariantId))),
            state with { LastIntent = nameof(NluIntent.ProductComparison) });
    }

    /// <summary>
    /// True when the referenced model and variant still exist and are currently active. The current
    /// quantity is deliberately not part of this decision: an active variant with no stock is still a real
    /// product the catalogue can price and report as unavailable, while a retired or missing row can no
    /// longer be answered about at all.
    /// </summary>
    private async Task<bool> IsCurrentlyActiveAsync(
        long modelId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var details = await catalogDetails.GetDetailsAsync(modelId, cancellationToken);

        if (details is not { IsActive: true } active)
        {
            return false;
        }

        return active.Variants.Any(variant => variant.VariantId == variantId && variant.IsActive);
    }

    /// <summary>
    /// A business-info question resolves to the approved Storefront keys it names through the deterministic
    /// allowlist. The stored answer is deliberately not read here: the renderer reads the current value,
    /// and the UX state never holds one. A question that names no approved concept, or that names more than
    /// one at once, is a clarification rather than an answer to a concept the customer may not have meant.
    /// </summary>
    private static ConversationRoute BusinessInfo(
        long conversationId,
        string customerExternalId,
        string? body,
        ConversationStateDocument state)
    {
        var keys = BusinessInfoScope.ResolveKeys(body);

        if (keys.Count == 0)
        {
            return Clarify(
                conversationId,
                customerExternalId,
                state,
                ConversationReasonCodes.BusinessInfoKeyNotResolved);
        }

        if (keys.Count > 1)
        {
            // Answering one of the two concepts would silently ignore half of what the customer asked.
            return Clarify(
                conversationId,
                customerExternalId,
                state,
                ConversationReasonCodes.BusinessInfoKeyAmbiguous);
        }

        return new ConversationRoute(
            Reply(conversationId, customerExternalId, ConversationResponseKind.BusinessInfo) with
            {
                StorefrontKey = keys[0],
            },
            state with { LastIntent = nameof(NluIntent.BusinessInfo) });
    }

    /// <summary>
    /// Maps the effective filters to the catalogue query. A hard budget stays exactly the ceiling the
    /// customer stated, and a resolution that cannot be read is a clarification instead of a silently
    /// dropped filter.
    /// </summary>
    private static (ProductSearchQuery? Query, string? ReasonCode) BuildQuery(ConversationStateFilters filters)
    {
        var (width, height, resolutionReason) = ParseResolution(filters.Resolution);

        if (resolutionReason is not null)
        {
            return (null, resolutionReason);
        }

        return (new ProductSearchQuery
        {
            ModelCode = filters.ModelCode,
            Brand = filters.Brand,
            SizeInches = filters.SizeInches,
            PanelType = filters.Panel,
            MinResolutionWidth = width,
            MinResolutionHeight = height,
            MinRefreshRate = filters.MinRefreshRate,
            RequiredPorts = filters.RequiredPorts,
            Grades = filters.Grades,
            Budget = BuildBudget(filters),
            UseCase = filters.UseCase,
        }, null);
    }

    /// <summary>
    /// The effective filters of a search: a field this turn states overrides the stored one, and every
    /// field it does not state is retained, so a follow-up refines the search instead of discarding it.
    /// An explicit budget replaces the stored budget, while a turn that states no budget at all keeps
    /// it — a hard ceiling can therefore never be dropped or widened by a refinement that did not
    /// mention money. Collections are replaced when this turn states them, never concatenated with the
    /// stored ones, because a stale port or grade is not the same request as a freshly stated one.
    /// </summary>
    private static ConversationStateFilters MergeFilters(
        ConversationStateFilters? stored,
        NluInterpretation interpretation)
    {
        var current = BuildFilters(interpretation);

        if (stored is null)
        {
            return current;
        }

        var (budgetType, target, min, max) = current.BudgetType is not (null or BudgetType.None)
            ? (current.BudgetType, current.BudgetTarget, current.BudgetMin, current.BudgetMax)
            : (stored.BudgetType, stored.BudgetTarget, stored.BudgetMin, stored.BudgetMax);

        return new ConversationStateFilters
        {
            Brand = Prefer(current.Brand, stored.Brand),
            ModelCode = Prefer(current.ModelCode, stored.ModelCode),
            SizeInches = current.SizeInches ?? stored.SizeInches,
            Panel = Prefer(current.Panel, stored.Panel),
            Resolution = Prefer(current.Resolution, stored.Resolution),
            MinRefreshRate = current.MinRefreshRate ?? stored.MinRefreshRate,
            RequiredPorts = current.RequiredPorts.Count > 0 ? current.RequiredPorts : stored.RequiredPorts,
            Grades = current.Grades.Count > 0 ? current.Grades : stored.Grades,
            BudgetType = budgetType,
            BudgetTarget = target,
            BudgetMin = min,
            BudgetMax = max,
            UseCase = Prefer(current.UseCase, stored.UseCase),
        };
    }

    private static string? Prefer(string? current, string? stored) =>
        string.IsNullOrWhiteSpace(current) ? stored : current;

    /// <summary>
    /// The budget shapes of docs/TECHNICAL.md section 11. The hard ceiling is carried through
    /// unchanged; no path here may raise it.
    /// </summary>
    private static ProductBudget? BuildBudget(ConversationStateFilters filters) => filters.BudgetType switch
    {
        BudgetType.Soft when filters.BudgetTarget is { } target => ProductBudget.Soft(target),
        BudgetType.Hard when filters.BudgetTarget is { } ceiling => ProductBudget.Hard(ceiling),
        BudgetType.Range when filters.BudgetMin is { } min && filters.BudgetMax is { } max =>
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
        string? body,
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
            var resolution = ConversationReferences.ResolveWithCustomerText(interpretation.Reference, body, state);

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
