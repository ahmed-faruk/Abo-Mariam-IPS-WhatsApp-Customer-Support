namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>What an admin commercial update did.</summary>
public enum CommercialUpdateOutcome
{
    /// <summary>The stored value changed, and the audit row was committed with it.</summary>
    Updated,

    /// <summary>The stored value already was the requested one, so nothing was written and nothing was audited.</summary>
    Unchanged,

    /// <summary>The variant does not exist, so nothing was written and nothing was audited.</summary>
    VariantNotFound,
}
