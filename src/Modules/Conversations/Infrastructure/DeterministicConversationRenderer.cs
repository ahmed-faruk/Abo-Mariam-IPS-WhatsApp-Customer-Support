using System.Globalization;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The deterministic renderer of docs/TECHNICAL.md section 12: the application writes the customer-facing
/// text, and every commercial value in it is read from Catalog or Storefront at the moment the reply is
/// built. The response intent carries identifiers, canonical keys and bounded reason codes only, so no
/// price, quantity, availability, grade, warranty, specification or business answer can come from the
/// model or from stored conversation state - there is nowhere for one to be.
/// </summary>
/// <remarks>
/// The wording is application-owned Egyptian Arabic with the English brand, model and technical terms the
/// catalogue stores, which is the primary language of docs/PLAN.md; no language is detected and no model
/// authors any of it. A valid business outcome the catalogue cannot satisfy - no current match, a retired
/// product, a Storefront key with no active value - is answered deterministically. An impossible internal
/// contract, such as a search reply with no effective query, is a programming defect and throws instead of
/// being hidden behind a silent no-response.
/// </remarks>
internal sealed class DeterministicConversationRenderer(
    ICatalogSearch catalogSearch,
    ICatalogProductDetails catalogDetails,
    IStorefrontBusinessInfo businessInfo) : IConversationRenderer
{
    /// <summary>
    /// How many products one search reply displays. The list is bounded so a reply stays readable and the
    /// reference positions stay meaningful; Catalog's own bound is the larger of the two.
    /// </summary>
    internal const int MaxDisplayedProducts = 5;

    private const string GreetingText =
        "أهلاً بيك! اسألني عن أي شاشة أو سعر أو مواصفات وأنا أساعدك.";

    private const string HandoffText =
        "تمام، هحوّلك لواحد من الفريق يرد عليك في أقرب وقت.";

    private const string UnsupportedMediaText =
        "معلش، بقبل رسايل نصية بس في الوقت الحالي. اكتب سؤالك وأنا أساعدك.";

    private const string OutOfScopeText =
        "معلش، أنا هنا أساعد في الشاشات والمنتجات بس.";

    private const string AiUnavailableText =
        "الخدمة مش متاحة دلوقتي. جرّب تاني بعد شوية، أو اطلب التحدث مع حد من الفريق.";

    private const string ClarificationText =
        "معلش مفهمتش. ممكن توضّحلي تاني إيه اللي تقصده؟";

    private const string ReferenceClarificationText =
        "تقصد أنهي منتج؟ ممكن تقولي رقمه في القائمة اللي عرضتها عليك.";

    private const string ShortlistClarificationText =
        "مش عندي قائمة معروضة أحدد منها. تحب أبحث لك من جديد؟";

    private const string BusinessInfoClarificationText =
        "تقصد أنهي معلومة بالظبط؟ مواعيد، عنوان، توصيل، دفع، ضمان، تليفون ولا استرجاع؟";

    private const string NoMatchText =
        "ملقتش حاجة مناسبة للطلب ده حالياً. تحب نغيّر الفلاتر شوية؟";

    private const string HardBudgetNoMatchText =
        "ملقتش أي منتج في الحد السعري اللي حددته، ومش هأعرض لك حاجة أغلى منه. تحب أوسّع الحد شوية؟";

    private const string ModelCodeNoMatchText =
        "الموديل ده مش موجود عندنا حالياً. تحب أدور لك على بديل بنفس المواصفات؟";

    private const string RetiredProductNoMatchText =
        "المنتج ده مش متاح عندنا حالياً. لو تحب أدور لك على بديل، قولي.";

    private const string UnavailableBusinessInfoText =
        "المعلومة دي مش متاحة عندنا دلوقتي. اسألني عن حاجة تانية أو اطلب التحدث مع حد من الفريق.";

    /// <summary>Builds the final text of one response intent from the current authoritative facts.</summary>
    /// <exception cref="InvalidOperationException">
    /// The intent is not a valid contract of this renderer, for example a search reply that carries no
    /// effective query.
    /// </exception>
    public async Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return intent.Kind switch
        {
            ConversationResponseKind.Greeting => Fixed(GreetingText),
            ConversationResponseKind.HumanHandoff => Fixed(HandoffText),
            ConversationResponseKind.UnsupportedMedia => Fixed(UnsupportedMediaText),
            ConversationResponseKind.OutOfScope => Fixed(OutOfScopeText),
            ConversationResponseKind.AiUnavailable => Fixed(AiUnavailableText),
            ConversationResponseKind.Clarification => Fixed(ClarificationTextFor(intent.ReasonCode)),
            ConversationResponseKind.NoMatch => Fixed(NoMatchTextFor(intent.ReasonCode)),
            ConversationResponseKind.ProductSearchResults => await SearchAsync(intent, cancellationToken),
            ConversationResponseKind.ProductDetails => await DetailsAsync(intent, cancellationToken),
            ConversationResponseKind.ProductComparison => await CompareAsync(intent, cancellationToken),
            ConversationResponseKind.Availability => await AvailabilityAsync(intent, cancellationToken),
            ConversationResponseKind.Price => await PriceAsync(intent, cancellationToken),
            ConversationResponseKind.BusinessInfo => await BusinessInfoAsync(intent, cancellationToken),
            _ => throw new InvalidOperationException(
                $"The response kind {intent.Kind} has no deterministic reply, so a reply must not be "
                + "invented for it."),
        };
    }

    /// <summary>
    /// Answers a search from the effective query the turn carried, run again against the catalogue now.
    /// Catalog owns every eligibility rule of that query - active model and variant, stock, hard and soft
    /// budget, required ports, grades and ranking - so re-running it is what makes the reply current: a
    /// product whose price left the hard ceiling, or whose stock or active state changed, simply is not in
    /// the result any more.
    /// </summary>
    private async Task<ConversationRenderResult> SearchAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var query = intent.SearchQuery
            ?? throw new InvalidOperationException(
                "A product-search reply must carry the effective search query it is answered from, because "
                + "the displayed products are the current catalogue results of that query.");

        var results = await catalogSearch.SearchAsync(query, cancellationToken);

        if (results.Count == 0)
        {
            // Nothing current qualifies, so nothing is displayed and the deterministic no-match answers
            // instead. The list the customer was already shown is not replaced by one they never saw.
            // A search bounded by a hard ceiling says so explicitly, because a reply that merely sounds
            // like a generic failure is exactly what the hard-budget gate rejects.
            return Fixed(
                query.Budget is { Type: BudgetType.Hard } ? HardBudgetNoMatchText : NoMatchText);
        }

        var displayed = results.Take(MaxDisplayedProducts).ToList();
        var lines = new List<string> { "لقيت لك الاختيارات دي من المتاح عندنا حالياً:" };

        for (var index = 0; index < displayed.Count; index++)
        {
            var result = displayed[index];

            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{index + 1}. {result.DisplayName} - {Money(result.Price)} جنيه"));
        }

        return ConversationRenderResult.Rendered(
            string.Join('\n', lines),
            displayed.Select(result => new ConversationDisplayedCandidate(result.ModelId, result.VariantId)));
    }

    /// <summary>Answers with the current facts of one identified product.</summary>
    private async Task<ConversationRenderResult> DetailsAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var identity = await LoadIdentityAsync(intent, cancellationToken);

        if (identity is null)
        {
            return Fixed(RetiredProductNoMatchText);
        }

        var (details, variant) = identity.Value;

        return Fixed(string.Join(
            '\n',
            $"{details.DisplayName} ({details.ModelCode})",
            $"المقاس: {Format(details.SizeInches)} بوصة | البانل: {details.PanelType}",
            $"الدقة: {details.ResolutionWidth}x{details.ResolutionHeight} | التحديث: {details.RefreshRate}Hz",
            $"المنافذ: {Ports(details)}",
            $"الحالة: {Availability(variant)}",
            $"السعر: {Money(variant.Price)} جنيه | الجريد: {variant.Grade}",
            $"الضمان: {variant.WarrantyDays} يوم"));
    }

    /// <summary>
    /// Compares the candidates the turn named, reloading each one from the catalogue first. A candidate
    /// whose model or variant is gone or retired is dropped instead of being compared from stale values;
    /// the survivors keep the order the customer heard. Fewer than two survivors is not a comparison, so
    /// the deterministic no-match answers instead.
    /// </summary>
    private async Task<ConversationRenderResult> CompareAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var candidates = Candidates(intent);

        if (candidates.Count < 2)
        {
            throw new InvalidOperationException(
                "A comparison reply must carry at least two model/variant pairs, because a comparison of "
                + "fewer is not a comparison.");
        }

        var lines = new List<string>();

        foreach (var (modelId, variantId) in candidates)
        {
            var identity = await LoadIdentityAsync(modelId, variantId, cancellationToken);

            if (identity is null)
            {
                continue;
            }

            var (details, variant) = identity.Value;

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{lines.Count + 1}. {details.DisplayName} ({details.ModelCode}) - "
                + $"{Money(variant.Price)} جنيه | الجريد: {variant.Grade} | "
                + $"الضمان: {variant.WarrantyDays} يوم | الحالة: {Availability(variant)}"));
        }

        return lines.Count < 2
            ? Fixed(RetiredProductNoMatchText)
            : Fixed(string.Join('\n', lines.Prepend("مقارنة بين:")));
    }

    /// <summary>
    /// Reports the current availability of one identified product. An active product with no stock is
    /// still a real product: it is reported as unavailable instead of being treated as one that does not
    /// exist, because the identity is what the customer asked about.
    /// </summary>
    private async Task<ConversationRenderResult> AvailabilityAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var identity = await LoadIdentityAsync(intent, cancellationToken);

        if (identity is null)
        {
            return Fixed(RetiredProductNoMatchText);
        }

        var (details, variant) = identity.Value;

        return Fixed($"{details.DisplayName}: الحالة دلوقتي {Availability(variant)}.");
    }

    /// <summary>
    /// Quotes the current price of one identified product, read from the catalogue at this moment. An
    /// active product with no stock still has a price, so its identity stays answerable.
    /// </summary>
    private async Task<ConversationRenderResult> PriceAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var identity = await LoadIdentityAsync(intent, cancellationToken);

        if (identity is null)
        {
            return Fixed(RetiredProductNoMatchText);
        }

        var (details, variant) = identity.Value;

        return Fixed(string.Create(
            CultureInfo.InvariantCulture,
            $"{details.DisplayName}: السعر الحالي {Money(variant.Price)} جنيه."));
    }

    /// <summary>
    /// Answers one approved business question with the current stored value. Only an allowlisted key is
    /// read, and a key whose row is missing, disabled or blank is answered with a safe deterministic
    /// message instead of falling back to an invented policy.
    /// </summary>
    private async Task<ConversationRenderResult> BusinessInfoAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var key = intent.StorefrontKey;

        if (string.IsNullOrWhiteSpace(key) || !BusinessInfoKeyNames.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"A business-info reply must name one approved Storefront key, but it named '{key}'.");
        }

        var stored = await businessInfo.GetByKeyAsync(key, cancellationToken);

        return stored is null || string.IsNullOrWhiteSpace(stored.AnswerAr)
            ? Fixed(UnavailableBusinessInfoText)
            : Fixed(stored.AnswerAr);
    }

    /// <summary>
    /// Reloads the one product a reply is about, or null when the catalogue no longer holds it as an
    /// answerable identity. The variant is looked up inside its own model, so a variant id that belongs to
    /// another model is never answered about, and only an active model with an active variant is a live
    /// identity: stock is reported separately and is never confused with existence.
    /// </summary>
    private async Task<(ProductDetails Details, ProductVariantDetails Variant)?> LoadIdentityAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken)
    {
        var (modelId, variantId) = OneProduct(intent);

        return await LoadIdentityAsync(modelId, variantId, cancellationToken);
    }

    private async Task<(ProductDetails Details, ProductVariantDetails Variant)?> LoadIdentityAsync(
        long modelId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var details = await catalogDetails.GetDetailsAsync(modelId, cancellationToken);

        if (details is not { IsActive: true })
        {
            return null;
        }

        var variant = details.Variants.FirstOrDefault(candidate => candidate.VariantId == variantId);

        return variant is { IsActive: true } ? (details, variant) : null;
    }

    private static (long ModelId, long VariantId) OneProduct(ConversationResponseIntent intent)
    {
        if (intent.ModelId is not { } modelId
            || intent.VariantId is not { } variantId
            || modelId <= 0
            || variantId <= 0)
        {
            throw new InvalidOperationException(
                $"A {intent.Kind} reply must identify one model and one variant, but it carried "
                + $"model '{intent.ModelId}' and variant '{intent.VariantId}'.");
        }

        return (modelId, variantId);
    }

    private static IReadOnlyList<(long ModelId, long VariantId)> Candidates(ConversationResponseIntent intent)
    {
        if (intent.ModelIds.Count != intent.VariantIds.Count)
        {
            throw new InvalidOperationException(
                "The candidate model and variant ids of a reply must be paired one to one, but the reply "
                + "carried a different number of each.");
        }

        return
        [
            .. intent.ModelIds.Select((modelId, index) =>
            {
                var variantId = intent.VariantIds[index];

                if (modelId <= 0 || variantId <= 0)
                {
                    throw new InvalidOperationException(
                        "The candidate identifiers of a reply must be positive catalogue ids.");
                }

                return (modelId, variantId);
            }),
        ];
    }

    private static string ClarificationTextFor(string? reasonCode) => reasonCode switch
    {
        ConversationReasonCodes.EmptyMessage or ConversationReasonCodes.InvalidModelOutput =>
            "معلش، مفهمتش رسالتك. ممكن تعيدها بصيغة أوضح؟",
        ConversationReasonCodes.ResolutionNotUnderstood =>
            "مش فاهم الدقة اللي تقصدها. ممكن تقولها زي 1920x1080؟",
        ConversationReasonCodes.ComparisonNeedsTwoCandidates =>
            "المقارنة محتاجة منتجين على الأقل من اللي عرضتهم عليك. تحب أبحث لك من جديد؟",
        ConversationReasonCodes.BusinessInfoKeyNotResolved or ConversationReasonCodes.BusinessInfoKeyAmbiguous =>
            BusinessInfoClarificationText,
        ConversationReferenceReasons.UnresolvedReference or ConversationReferenceReasons.CurrentReferenceMissing =>
            ReferenceClarificationText,
        ConversationReferenceReasons.ShortlistEmpty or ConversationReferenceReasons.ShortlistPositionMissing =>
            ShortlistClarificationText,
        _ => ClarificationText,
    };

    private static string NoMatchTextFor(string? reasonCode) => reasonCode switch
    {
        ConversationReasonCodes.ModelCodeNotAvailable => ModelCodeNoMatchText,
        ConversationReasonCodes.ProductNoLongerAvailable => RetiredProductNoMatchText,
        _ => NoMatchText,
    };

    private static ConversationRenderResult Fixed(string body) => ConversationRenderResult.Rendered(body);

    private static string Availability(ProductVariantDetails variant) =>
        variant.Quantity > 0 ? "متوفر" : "غير متوفر";

    private static string Money(decimal price) => Format(price);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Ports(ProductDetails details) =>
        details.Ports.Count == 0
            ? "مفيش منافذ مسجلة"
            : string.Join(", ", details.Ports.Select(port => port.PortType));
}
