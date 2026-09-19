namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// The documented structured-NLU contract: docs/TECHNICAL.md section 8.3 (schema) and
/// section 9 (intent routing), plus docs/PLAN.md section 8 (conceptual output).
/// These lists exist so the harness, the dataset and the tests cannot drift apart.
/// </summary>
public static class NluContract
{
    /// <summary>Documented intent names from docs/TECHNICAL.md section 9.</summary>
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

    /// <summary>Documented budget types from docs/TECHNICAL.md section 8.3.</summary>
    public static readonly IReadOnlyList<string> BudgetTypes = ["None", "Soft", "Hard", "Range"];

    /// <summary>
    /// Every documented structured field, in the order of docs/TECHNICAL.md section 8.3.
    /// Field statistics are reported in this order.
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

    /// <summary>Fields the schema requires (docs/TECHNICAL.md section 8.3).</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
        ["intent", "requiredPorts", "grades", "budgetType"];

    /// <summary>Curated use-case tags from docs/PLAN.md section 6 (UC-04).</summary>
    public static readonly IReadOnlyList<string> CuratedUseCases =
        ["Programming", "Office", "Gaming", "Design", "CCTV"];

    public const string SchemaVersion = "nlu-output-v1";
    /// <summary>v3: extraction-only rules, null/empty-collection contract and intent routing spelled out.</summary>
    public const string PromptVersion = "nlu-system-prompt-v3";
    public const string BenchmarkFormatVersion = "1";
    public const string HarnessVersion = "issue8-nlu-benchmark-1";
    public const string SourceDocument = "docs/TECHNICAL.md v3.2 section 8/9/28, docs/PLAN.md v3.2 section 13.3";
}
