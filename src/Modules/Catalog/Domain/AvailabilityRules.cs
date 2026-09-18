namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The availability rule of docs/PLAN.md section 7 and docs/TECHNICAL.md section 10: a model counts
/// as available only when the model is active, the variant is active and the variant has stock.
/// </summary>
public static class AvailabilityRules
{
    public static bool IsAvailable(bool isModelActive, bool isVariantActive, int quantity) =>
        isModelActive && isVariantActive && quantity > 0;
}
