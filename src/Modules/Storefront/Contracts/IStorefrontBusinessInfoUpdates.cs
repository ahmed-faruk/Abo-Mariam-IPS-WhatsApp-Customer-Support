namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>
/// The admin write side of the business information. An update only ever changes an existing approved
/// row, and the values it stores are the ones an admin supplied.
/// </summary>
public interface IStorefrontBusinessInfoUpdates
{
    /// <summary>Updates one stored approved business-info row.</summary>
    Task<BusinessInfoUpdateOutcome> UpdateAsync(
        BusinessInfoUpdate update,
        CancellationToken cancellationToken = default);
}

/// <summary>A business-info change requested by an admin.</summary>
/// <param name="Key">The approved key to change. Any other key is rejected before persistence.</param>
/// <param name="AnswerAr">The new Arabic answer. It must not be blank.</param>
/// <param name="AnswerEn">The new English answer, or null to clear it.</param>
/// <param name="IsActive">Whether the row is served to customers.</param>
public sealed record BusinessInfoUpdate(string Key, string AnswerAr, string? AnswerEn, bool IsActive);
