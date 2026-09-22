namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>
/// The canonical names of the approved business-info keys, exposed so a caller outside Storefront can
/// name one without referencing a Storefront domain type. These constants are the single source of
/// truth: the module's own allowlist is built from them, so a contract name and a stored key can never
/// drift apart. No new key is introduced here.
/// </summary>
public static class BusinessInfoKeyNames
{
    /// <summary>Opening hours of the shop.</summary>
    public const string WorkingHours = "WorkingHours";

    /// <summary>The address and location of the shop.</summary>
    public const string Address = "Address";

    /// <summary>The delivery policy.</summary>
    public const string Delivery = "Delivery";

    /// <summary>The accepted payment methods.</summary>
    public const string PaymentMethods = "PaymentMethods";

    /// <summary>The warranty policy.</summary>
    public const string Warranty = "Warranty";

    /// <summary>The contact phone number.</summary>
    public const string ContactPhone = "ContactPhone";

    /// <summary>The return and exchange policy.</summary>
    public const string ReturnExchangePolicy = "ReturnExchangePolicy";

    /// <summary>Every approved key, in the order docs/PLAN.md section 6 lists the concepts.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        WorkingHours,
        Address,
        Delivery,
        PaymentMethods,
        Warranty,
        ContactPhone,
        ReturnExchangePolicy,
    ];
}
