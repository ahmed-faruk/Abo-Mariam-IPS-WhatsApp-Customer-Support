using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin.Pages;

/// <summary>
/// One conversation's current mode and its chronological transcript. The transcript is correct only
/// for the reset-controlled single-conversation demo state: inbound rows are read by customer, because
/// Messaging does not link them to a conversation (src/Host.Web/Admin/README.md).
/// </summary>
public sealed class ConversationDetailModel(
    IConversationAdminReads conversations,
    IMessagingTranscriptReads transcripts) : PageModel
{
    public ConversationListRow? Conversation { get; private set; }

    public IReadOnlyList<TranscriptLine> Lines { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        Conversation = await conversations.GetAsync(id, cancellationToken);

        if (Conversation is null)
        {
            return NotFound();
        }

        Lines = AdminTranscriptComposer.Compose(
            await transcripts.ListInboundByCustomerAsync(Conversation.CustomerExternalId, cancellationToken),
            await transcripts.ListOutboundByConversationAsync(id, cancellationToken));

        return Page();
    }
}
