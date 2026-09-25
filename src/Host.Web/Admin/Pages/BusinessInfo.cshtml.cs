using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>
/// The approved business-info answers, read through the customer-facing Storefront contract. Only
/// WorkingHours is editable, through the existing Storefront update contract, so the next customer
/// read observes the new stored value.
/// </summary>
public sealed class BusinessInfoModel(
    IStorefrontBusinessInfo businessInfo,
    IStorefrontBusinessInfoUpdates updates) : PageModel
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

    public async Task<IActionResult> OnPostWorkingHoursAsync(string? answerAr, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(answerAr))
        {
            return BadRequest();
        }

        var current = await businessInfo.GetByKeyAsync(BusinessInfoKeyNames.WorkingHours, cancellationToken);
        var outcome = await updates.UpdateAsync(
            new BusinessInfoUpdate(BusinessInfoKeyNames.WorkingHours, answerAr, current?.AnswerEn, true),
            cancellationToken);

        return outcome == BusinessInfoUpdateOutcome.NotFound ? NotFound() : RedirectToPage();
    }
}
