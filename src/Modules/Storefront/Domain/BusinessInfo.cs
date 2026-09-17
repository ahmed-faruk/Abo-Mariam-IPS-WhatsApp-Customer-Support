namespace WhatsAppMonitorAssistant.Modules.Storefront.Domain;

/// <summary>Allowlisted business FAQ answer owned by the Storefront module.</summary>
public sealed class BusinessInfo
{
    public long Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string AnswerAr { get; set; } = string.Empty;

    public string? AnswerEn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; }
}
