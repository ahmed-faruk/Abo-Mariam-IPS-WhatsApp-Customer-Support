using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Domain;

/// <summary>
/// The one central allowlist of business-info keys the Storefront module accepts. docs/PLAN.md
/// section 6 lists the approved concepts but no persisted strings, so the module owns exactly one
/// canonical key per concept. Reads and writes both canonicalize through this type, and no other
/// code may add a key.
/// </summary>
public static class BusinessInfoKeys
{
    /// <summary>Opening hours of the shop.</summary>
    public const string WorkingHours = BusinessInfoKeyNames.WorkingHours;

    /// <summary>The address and location of the shop.</summary>
    public const string Address = BusinessInfoKeyNames.Address;

    /// <summary>The delivery policy.</summary>
    public const string Delivery = BusinessInfoKeyNames.Delivery;

    /// <summary>The accepted payment methods.</summary>
    public const string PaymentMethods = BusinessInfoKeyNames.PaymentMethods;

    /// <summary>The warranty policy.</summary>
    public const string Warranty = BusinessInfoKeyNames.Warranty;

    /// <summary>The contact phone number.</summary>
    public const string ContactPhone = BusinessInfoKeyNames.ContactPhone;

    /// <summary>The return and exchange policy.</summary>
    public const string ReturnExchangePolicy = BusinessInfoKeyNames.ReturnExchangePolicy;

    /// <summary>Every approved key, in the order docs/PLAN.md section 6 lists the concepts.</summary>
    public static IReadOnlyList<string> All { get; } = BusinessInfoKeyNames.All;

    /// <summary>
    /// Returns the canonical key of an approved key, ignoring outer whitespace and case, so
    /// " workinghours " resolves to <see cref="WorkingHours"/>. Anything else is null: the module
    /// never invents a key, accepts an alias or fuzzy-matches one.
    /// </summary>
    public static string? Canonicalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();

        foreach (var canonical in All)
        {
            if (string.Equals(canonical, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return canonical;
            }
        }

        return null;
    }

    /// <summary>The canonical key, or an <see cref="ArgumentException"/> when the key is not approved.</summary>
    public static string RequireAllowed(string? key) =>
        Canonicalize(key)
        ?? throw new ArgumentException(
            $"'{key}' is not an allowed business info key. The allowed keys are: "
            + $"{string.Join(", ", All)}.",
            nameof(key));
}
