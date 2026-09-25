using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>The Admin Lite catalogue grid: model, grade, price and quantity of every variant.</summary>
public sealed class CatalogModel(ICatalogAdminGrid grid) : PageModel
{
    public IReadOnlyList<CatalogGridRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await grid.ListVariantsAsync(cancellationToken);
    }
}
