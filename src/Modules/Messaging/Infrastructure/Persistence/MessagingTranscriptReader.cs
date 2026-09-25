using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>The read-only Admin Lite transcript queries. Every order ends with the row id, so it is total.</summary>
internal sealed class MessagingTranscriptReader(MessagingDbContext dbContext) : IMessagingTranscriptReads
{
    private const int MaxRows = 500;

    public async Task<IReadOnlyList<InboundTranscriptEntry>> ListInboundByCustomerAsync(
        string customerExternalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerExternalId);

        return await dbContext.InboxMessages
            .AsNoTracking()
            .Where(message => message.CustomerExternalId == customerExternalId)
            .OrderBy(message => message.ProviderTimestamp)
            .ThenBy(message => message.Id)
            .Take(MaxRows)
            .Select(message => new InboundTranscriptEntry(
                message.Id,
                message.ProviderMessageId,
                message.MessageType,
                message.Body,
                message.ProviderTimestamp))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboundTranscriptEntry>> ListOutboundByConversationAsync(
        long conversationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(MaxRows)
            .Select(message => new OutboundTranscriptEntry(
                message.Id,
                message.Sender,
                message.Body,
                message.DeliveryStatus,
                message.CreatedAt))
            .ToListAsync(cancellationToken);
}
