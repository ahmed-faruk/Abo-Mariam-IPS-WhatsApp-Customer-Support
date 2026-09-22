using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The adapter the Messaging Inbox worker resolves. It translates the claimed queue message into the
/// Conversations turn contract, so the worker keeps knowing nothing about business orchestration and
/// no queue type crosses into the Conversations contracts.
/// </summary>
internal sealed class MessagingInboundMessageProcessor(IProcessInboundTurn processor) : IInboundMessageProcessor
{
    public async Task ProcessAsync(
        ClaimedInboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await processor.ProcessAsync(
            new InboundTurn(
                message.ProviderMessageId,
                message.CustomerExternalId,
                message.MessageType,
                message.ProviderTimestamp,
                message.Body,
                message.ConversationId),
            cancellationToken);
    }
}
