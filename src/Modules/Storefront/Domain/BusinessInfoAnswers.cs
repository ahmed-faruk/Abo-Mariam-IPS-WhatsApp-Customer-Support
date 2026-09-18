namespace WhatsAppMonitorAssistant.Modules.Storefront.Domain;

/// <summary>
/// The value rules of one stored business-info row: the Arabic answer is required, the English answer
/// is optional, and both only lose their outer whitespace. The module never rewrites the content, so
/// the stored text is exactly what an admin wrote and never generated policy text.
/// </summary>
public static class BusinessInfoAnswers
{
    /// <summary>
    /// The required Arabic answer, trimmed, or an <see cref="ArgumentException"/> when it is absent or
    /// blank, because an empty business answer would answer a customer with nothing.
    /// </summary>
    public static string RequireAnswerAr(string? answerAr)
    {
        if (string.IsNullOrWhiteSpace(answerAr))
        {
            throw new ArgumentException(
                "The Arabic answer is required and must not be blank.",
                nameof(answerAr));
        }

        return answerAr.Trim();
    }

    /// <summary>
    /// The optional English answer, trimmed. A missing or blank one normalizes to null instead of an
    /// empty stored value, so "no English answer" has exactly one representation.
    /// </summary>
    public static string? NormalizeAnswerEn(string? answerEn) =>
        string.IsNullOrWhiteSpace(answerEn) ? null : answerEn.Trim();
}
