using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>The approved business-info answers, read through the customer-facing Storefront contract.</summary>
public sealed class BusinessInfoModel(IStorefrontBusinessInfo businessInfo) : PageModel
{
    public IReadOnlyList<(string Key, BusinessInfoValue? Value)> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(string, BusinessInfoValue?)>();

        foreach (var key in BusinessInfoKeyNames.All)
        {
            rows.Add((key, await businessInfo.GetByKeyAsync(key, cancellationToken)));
        }

        Rows = rows;
    }
}
