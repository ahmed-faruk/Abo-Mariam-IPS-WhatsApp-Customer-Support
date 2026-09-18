namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>
/// One stored business-info row as the Storefront module reads or writes it. Every value is the
/// authoritative PostgreSQL value; no caller may answer a customer from model-generated policy text.
/// </summary>
/// <param name="Key">The canonical allowlisted key of the row.</param>
/// <param name="AnswerAr">The stored Arabic answer.</param>
/// <param name="AnswerEn">The stored English answer, when there is one.</param>
/// <param name="IsActive">Whether the row is currently served to customers.</param>
/// <param name="UpdatedAt">The stored <c>updated_at</c> timestamp written by PostgreSQL.</param>
public sealed record BusinessInfoValue(
    string Key,
    string AnswerAr,
    string? AnswerEn,
    bool IsActive,
    DateTime UpdatedAt);
