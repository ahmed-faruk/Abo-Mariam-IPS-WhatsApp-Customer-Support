using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Tools.DemoOps;

/// <summary>
/// The frozen controlled-demo dataset of Issue #15. The catalogue is representative demo stock, not
/// real client inventory, and is shaped so the DEMO-04 hard-budget search ranks the Dell P2419H first
/// without any ranking change. The business-info answers are the customer-facing texts approved for
/// the demo (H4b). The tolerances are controlled-demo values only, never pilot or production defaults.
/// </summary>
public static class DemoDataset
{
    public const decimal DemoSizeToleranceInches = 0.5m;

    public const decimal DemoSoftBudgetTolerance = 0.10m;

    /// <summary>The model the DEMO-04 hard-budget turn must show first.</summary>
    public const string KeyModelCode = "P2419H";

    public const decimal HardBudgetCeiling = 2500m;

    public const decimal SoftBudgetTarget = 3000m;

    /// <summary>The effective DEMO-04 search: Dell, 24 inch, IPS, HDMI, never above 2500.</summary>
    public static ProductSearchQuery HardBudgetQuery { get; } = new()
    {
        Brand = "Dell",
        SizeInches = 24m,
        PanelType = "IPS",
        RequiredPorts = ["HDMI"],
        Budget = ProductBudget.Hard(HardBudgetCeiling),
    };

    /// <summary>The effective DEMO-03 search: the same filters with a soft budget around 3000.</summary>
    public static ProductSearchQuery SoftBudgetQuery { get; } = HardBudgetQuery with
    {
        Budget = ProductBudget.Soft(SoftBudgetTarget),
    };

    /// <summary>The highest price the DEMO-03 soft budget may show.</summary>
    public static decimal SoftBudgetCeiling => SoftBudgetTarget * (1 + DemoSoftBudgetTolerance);

    public static IReadOnlyList<DemoModelSeed> Catalogue { get; } =
    [
        Model("P2419H", "Dell", "P2419H", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office, Programming], A(2400m, 3, 90)),
        Model("P2422H", "Dell", "P2422H", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office, Programming], A(3100m, 2, 90), B(2700m, 1, 30)),
        Model("U2419H", "Dell", "UltraSharp U2419H", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp], [Design, Office], A(3400m, 1, 90), B(2950m, 2, 30)),
        Model("SE2419H", "Dell", "SE2419H", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Vga], [Office], A(2150m, 4, 60)),
        Model("P2418D", "Dell", "P2418D", 23.8m, "IPS", 2560, 1440, 60, [Hdmi, Dp], [Design, Programming], A(3600m, 1, 90)),
        Model("E2420H", "Dell", "E2420H", 23.8m, "IPS", 1920, 1080, 60, [Dp, Vga], [Office], A(1900m, 3, 60)),
        Model("E2416H", "Dell", "E2416H", 24.0m, "TN", 1920, 1080, 60, [Dp, Vga], [Office, "CCTV"], A(1500m, 5, 30)),
        Model("S2421H", "Dell", "S2421H", 23.8m, "IPS", 1920, 1080, 75, [new("HDMI", 2)], [Office], A(2700m, 2, 60), B(2450m, 1, 30)),
        Model("P2719H", "Dell", "P2719H", 27.0m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office], A(3900m, 2, 90)),
        Model("U2719D", "Dell", "UltraSharp U2719D", 27.0m, "IPS", 2560, 1440, 60, [Hdmi, Dp], [Design], A(5200m, 1, 90)),
        Model("P2219H", "Dell", "P2219H", 21.5m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office], A(1800m, 3, 60)),
        Model("24ES", "HP", "24es", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Vga], [Office], A(2200m, 2, 60)),
        Model("E24G4", "HP", "E24 G4", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office, Programming], A(2600m, 2, 90), B(2200m, 1, 30)),
        Model("E243", "HP", "EliteDisplay E243", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office], A(2300m, 2, 60)),
        Model("T24I-10", "Lenovo", "ThinkVision T24i-10", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office, Programming], A(2350m, 2, 60)),
        Model("P27H-20", "Lenovo", "ThinkVision P27h-20", 27.0m, "IPS", 2560, 1440, 60, [Hdmi, Dp, new("USB-C", 1)], [Design, Programming], A(4800m, 1, 90)),
        Model("24MK600M", "LG", "24MK600M", 23.8m, "IPS", 1920, 1080, 75, [Hdmi, Vga], [Office], A(2050m, 3, 60)),
        Model("S24F350", "Samsung", "S24F350", 23.5m, "Other", 1920, 1080, 60, [Hdmi, Vga], [Office], A(1700m, 4, 30)),
        Model("24G2", "AOC", "24G2", 23.8m, "IPS", 1920, 1080, 144, [Hdmi, Dp, Vga], ["Gaming"], A(3300m, 2, 60), B(2900m, 1, 30)),
        Model("GW2480", "BenQ", "GW2480", 23.8m, "IPS", 1920, 1080, 60, [Hdmi, Dp, Vga], [Office, Programming], A(2250m, 2, 60)),
    ];

    /// <summary>The approved customer-facing business information (H4b), one row per approved key.</summary>
    public static IReadOnlyList<BusinessInfoUpdate> BusinessInfo { get; } =
    [
        Answer(BusinessInfoKeyNames.WorkingHours, "مواعيدنا من السبت للخميس من 11 الصبح لحد 10 بالليل، والجمعة من 2 الضهر لحد 10 بالليل."),
        Answer(BusinessInfoKeyNames.Address, "المحل في وسط البلد، القاهرة. ابعتلنا وهنبعتلك اللوكيشن."),
        Answer(BusinessInfoKeyNames.Delivery, "بنوصّل لكل محافظات مصر، ومصاريف الشحن بتتحدد حسب المحافظة."),
        Answer(BusinessInfoKeyNames.PaymentMethods, "بنقبل كاش، وإنستاباي، وفودافون كاش."),
        Answer(BusinessInfoKeyNames.Warranty, "كل شاشة عليها ضمان حسب الفرز، والتفاصيل مكتوبة مع كل منتج."),
        Answer(BusinessInfoKeyNames.ContactPhone, "للتواصل: ٠١٠٩١٠٠٨٨١٥"),
        Answer(BusinessInfoKeyNames.ReturnExchangePolicy, "الاسترجاع أو الاستبدال خلال 3 أيام من الاستلام لو الشاشة بنفس حالتها."),
    ];

    private const string Office = "Office";

    private const string Programming = "Programming";

    private const string Design = "Design";

    private static DemoPortSeed Hdmi => new("HDMI", 1);

    private static DemoPortSeed Dp => new("DisplayPort", 1);

    private static DemoPortSeed Vga => new("VGA", 1);

    /// <summary>The SKU of a demo variant: DEMO-, the model code without dashes, and the grade.</summary>
    public static string Sku(string modelCode, string grade) =>
        $"DEMO-{modelCode.Replace("-", string.Empty, StringComparison.Ordinal)}-{grade}";

    private static DemoModelSeed Model(
        string modelCode,
        string brand,
        string model,
        decimal sizeInches,
        string panelType,
        int resolutionWidth,
        int resolutionHeight,
        int refreshRate,
        DemoPortSeed[] ports,
        string[] tags,
        params (string Grade, decimal Price, int Quantity, int WarrantyDays)[] variants) =>
        new(
            ModelCode: modelCode,
            Brand: brand,
            Model: model,
            DisplayName: $"{brand} {model}",
            SizeInches: sizeInches,
            PanelType: panelType,
            ResolutionWidth: resolutionWidth,
            ResolutionHeight: resolutionHeight,
            RefreshRate: refreshRate,
            Description: null,
            SearchTags: tags,
            Ports: ports,
            Variants:
            [
                .. variants.Select(variant => new DemoVariantSeed(
                    Sku(modelCode, variant.Grade),
                    variant.Grade,
                    variant.Price,
                    variant.Quantity,
                    variant.WarrantyDays,
                    WarrantyNotes: null,
                    CosmeticNotes: null)),
            ]);

    private static (string, decimal, int, int) A(decimal price, int quantity, int warrantyDays) =>
        ("A", price, quantity, warrantyDays);

    private static (string, decimal, int, int) B(decimal price, int quantity, int warrantyDays) =>
        ("B", price, quantity, warrantyDays);

    private static BusinessInfoUpdate Answer(string key, string answerAr) => new(key, answerAr, null, true);
}
