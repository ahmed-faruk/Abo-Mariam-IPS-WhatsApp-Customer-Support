namespace WhatsAppMonitorAssistant.Modules.Catalog.Domain;

/// <summary>
/// The monitor size range enforced by the <c>ck_model_size</c> constraint of docs/TECHNICAL.md
/// section 6.1. Configuration validation uses it as the safety bound of a size tolerance: a tolerance
/// larger than the whole range could only switch the size filter off.
/// </summary>
public static class ModelSizeBounds
{
    public const decimal Min = 10m;

    public const decimal Max = 60m;

    /// <summary>The largest size difference any two stored models can have.</summary>
    public static decimal LargestDifference => Max - Min;
}
