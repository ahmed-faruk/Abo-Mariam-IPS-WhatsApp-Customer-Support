namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The frozen structured-NLU identities and vocabularies of docs/TECHNICAL.md sections 8.2 and 8.3.
/// The values here are the production copy of the measured Controlled Demo Candidate contract, so no
/// production project has to reference the historical benchmark executable.
/// </summary>
public static class NluContract
{
    /// <summary>The documented intent names of docs/TECHNICAL.md section 9, in documentation order.</summary>
    public static readonly IReadOnlyList<string> Intents =
    [
        "Greeting",
        "ProductSearch",
        "ProductDetails",
        "ProductComparison",
        "AvailabilityCheck",
        "PriceCheck",
        "BusinessInfo",
        "HumanHandoff",
        "UnsupportedMedia",
        "OutOfScope",
    ];

    /// <summary>The documented budget types of docs/TECHNICAL.md section 8.3.</summary>
    public static readonly IReadOnlyList<string> BudgetTypes = ["None", "Soft", "Hard", "Range"];

    /// <summary>
    /// Every documented structured field, in the order of docs/TECHNICAL.md section 8.3. The schema
    /// sends this exact set, so an extra model-authored key such as a price never survives validation.
    /// </summary>
    public static readonly IReadOnlyList<string> Fields =
    [
        "intent",
        "brand",
        "modelCode",
        "sizeInches",
        "panel",
        "resolution",
        "minRefreshRate",
        "requiredPorts",
        "grades",
        "budgetType",
        "budgetTarget",
        "budgetMin",
        "budgetMax",
        "useCase",
        "reference",
    ];

    /// <summary>The fields the schema requires (docs/TECHNICAL.md section 8.3).</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
        ["intent", "requiredPorts", "grades", "budgetType"];

    /// <summary>The version of the structured-output schema the adapter sends.</summary>
    public const string SchemaVersion = "nlu-output-v1";

    /// <summary>The version of the frozen structured-NLU prompt.</summary>
    public const string PromptVersion = "nlu-system-prompt-v3";

    /// <summary>
    /// SHA-256 over the runtime UTF-8 bytes of the production prompt, which is the frozen
    /// <c>PromptSha256</c> of docs/TECHNICAL.md section 8.2.
    /// </summary>
    public const string PromptSha256 =
        "2139120c08b3ad01a5389f986ae6a4a7e884da372591a951a6efaea225237d3a";

    /// <summary>
    /// SHA-256 over the bytes of the structured-output schema the adapter sends, which is the frozen
    /// schema hash recorded by Issue #8 and docs/TECHNICAL.md section 8.2.
    /// </summary>
    public const string SchemaSha256 =
        "fb9eacee28dcf31f6438fbe63092a8b48abb42cf5c592f4edd06874b2f1d4302";
}
