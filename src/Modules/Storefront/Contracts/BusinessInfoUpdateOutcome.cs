namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>What an admin business-info update did.</summary>
public enum BusinessInfoUpdateOutcome
{
    /// <summary>The stored values changed and were committed.</summary>
    Updated,

    /// <summary>The stored values already were the requested ones, so nothing was written.</summary>
    Unchanged,

    /// <summary>The approved key has no stored row, and an update never creates one.</summary>
    NotFound,
}
