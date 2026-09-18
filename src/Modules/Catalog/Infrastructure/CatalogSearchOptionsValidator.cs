using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;

/// <summary>
/// Rejects a search policy that could not answer a query honestly: a negative tolerance is not a
/// distance, and a result maximum below one would make every search fail.
/// </summary>
internal sealed class CatalogSearchOptionsValidator : IValidateOptions<CatalogSearchOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.SizeToleranceInches < 0)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.SizeToleranceInches)}' must not be "
                + $"negative but was {options.SizeToleranceInches}.");
        }

        if (options.SoftBudgetTolerance < 0)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.SoftBudgetTolerance)}' must not be "
                + $"negative but was {options.SoftBudgetTolerance}.");
        }

        if (options.MaxResults < 1)
        {
            failures.Add(
                $"The catalog search setting '{nameof(CatalogSearchOptions.MaxResults)}' must be greater than "
                + $"zero but was {options.MaxResults}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
