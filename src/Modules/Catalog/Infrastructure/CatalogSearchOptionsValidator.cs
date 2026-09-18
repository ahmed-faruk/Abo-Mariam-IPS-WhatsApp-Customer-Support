using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

/// <summary>
/// Rejects a search policy that could not answer a query honestly. Both tolerances are required,
/// because neither docs/PLAN.md nor docs/TECHNICAL.md defines a numeric value for them, and a
/// tolerance outside its safe range is a configuration error rather than a business policy.
/// </summary>
internal sealed class CatalogSearchOptionsValidator : IValidateOptions<CatalogSearchOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.SizeToleranceInches is not { } sizeTolerance)
        {
            failures.Add(Missing(nameof(CatalogSearchOptions.SizeToleranceInches)));
        }
        else if (sizeTolerance < 0 || sizeTolerance > ModelSizeBounds.LargestDifference)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.SizeToleranceInches)}' must be "
                + $"between 0 and {ModelSizeBounds.LargestDifference} inches, which is the size range of the "
                + $"stored models, but was {sizeTolerance}.");
        }

        if (options.SoftBudgetTolerance is not { } softBudgetTolerance)
        {
            failures.Add(Missing(nameof(CatalogSearchOptions.SoftBudgetTolerance)));
        }
        else if (softBudgetTolerance < 0 || softBudgetTolerance >= 1)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.SoftBudgetTolerance)}' must be at "
                + "least 0 and below 1, because it is a fraction of the stated budget: a negative value would "
                + $"under-cut the stated budget and 1 or more would at least double it, but it was "
                + $"{softBudgetTolerance}.");
        }

        if (options.MaxResults < 1 || options.MaxResults > CatalogSearchPolicy.MaximumResults)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.MaxResults)}' must be between 1 and "
                + $"{CatalogSearchPolicy.MaximumResults}, which is the search policy of "
                + "docs/TECHNICAL.md section 10, but was {options.MaxResults}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string Missing(string setting) =>
        $"The catalog search setting '{setting}' is not configured. Set it explicitly under "
        + $"'{CatalogSearchOptions.ConfigurationSectionName}'; the module applies no tolerance default "
        + "because the project baseline defines none.";
}
