using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>
/// The Admin Lite catalogue grid: model, grade, price and quantity of every variant. Price and quantity
/// are edited only through the audited catalogue commercial-update contract, under the demo actor.
/// </summary>
public sealed class CatalogModel(ICatalogAdminGrid grid, ICatalogCommercialUpdates updates) : PageModel
{
    /// <summary>The audit actor of every Admin Lite catalogue change.</summary>
    public const string Actor = "demo-operator";

    /// <summary>The largest price the catalogue's numeric(12,2) price column can store.</summary>
    public const decimal MaxPrice = 9_999_999_999.99m;

    public IReadOnlyList<CatalogGridRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await grid.ListVariantsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPriceAsync(long variantId, string? price, CancellationToken cancellationToken)
    {
        if (!decimal.TryParse(price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value > MaxPrice)
        {
            return BadRequest();
        }

        return await UpdateAsync(() => updates.UpdatePriceAsync(new VariantPriceUpdate(variantId, value, Actor), cancellationToken));
    }

    public async Task<IActionResult> OnPostQuantityAsync(long variantId, string? quantity, CancellationToken cancellationToken)
    {
        if (!int.TryParse(quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return BadRequest();
        }

        return await UpdateAsync(() => updates.UpdateQuantityAsync(new VariantQuantityUpdate(variantId, value, Actor), cancellationToken));
    }

    /// <summary>
    /// Runs one audited update. A value the catalogue rules reject, such as an out-of-range price or a
    /// non-positive variant id, is a bad request; an unknown variant is not found.
    /// </summary>
    private async Task<IActionResult> UpdateAsync(Func<Task<CommercialUpdateOutcome>> update)
    {
        CommercialUpdateOutcome outcome;

        try
        {
            outcome = await update();
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        return outcome == CommercialUpdateOutcome.VariantNotFound ? NotFound() : RedirectToPage();
    }
}
