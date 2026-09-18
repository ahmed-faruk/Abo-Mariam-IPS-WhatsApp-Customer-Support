using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Features.UpdateBusinessInfo;

/// <summary>
/// The persistence port of the business-info update. The module-internal implementation owns the
/// transaction that reads the stored row, locks it and writes the change.
/// </summary>
internal interface IBusinessInfoUpdateStore
{
    Task<BusinessInfoUpdateOutcome> UpdateAsync(
        BusinessInfoChange change,
        CancellationToken cancellationToken);
}

/// <summary>One validated and normalized change, ready to be written to an existing row.</summary>
/// <param name="Key">The canonical approved key of the row to change.</param>
/// <param name="AnswerAr">The trimmed Arabic answer.</param>
/// <param name="AnswerEn">The trimmed English answer, or null when there is none.</param>
/// <param name="IsActive">The requested active state.</param>
internal sealed record BusinessInfoChange(string Key, string AnswerAr, string? AnswerEn, bool IsActive);
