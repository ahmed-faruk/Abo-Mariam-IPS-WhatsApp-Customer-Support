using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>The Admin Lite conversation list: customer, current mode and last activity.</summary>
public sealed class ConversationsModel(IConversationAdminReads conversations) : PageModel
{
    public IReadOnlyList<ConversationListRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await conversations.ListAsync(cancellationToken);
    }
}
