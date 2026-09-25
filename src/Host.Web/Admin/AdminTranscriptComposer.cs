using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Admin;

/// <summary>One line of an Admin Lite transcript.</summary>
/// <param name="Inbound">True for a customer message, false for a reply.</param>
/// <param name="At">The inbound provider timestamp, or the reply's creation time.</param>
/// <param name="Text">The message text; a non-text inbound message shows its type.</param>
/// <param name="Detail">The inbound message type or the reply's sender and delivery status.</param>
public sealed record TranscriptLine(bool Inbound, DateTime At, string Text, string Detail);

/// <summary>
/// Merges a customer's inbound messages and a conversation's replies into one deterministic order: by
/// time, inbound before outbound at the same instant, then by row id. There is no lower bound from the
/// conversation start, because the first inbound message is stored before its conversation exists.
/// </summary>
public static class AdminTranscriptComposer
{
    public static IReadOnlyList<TranscriptLine> Compose(
        IReadOnlyList<InboundTranscriptEntry> inbound,
        IReadOnlyList<OutboundTranscriptEntry> outbound)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(outbound);

        var lines = inbound
            .Select(message => (
                At: message.ProviderTimestamp,
                Order: 0,
                Id: message.InboxMessageId,
                Line: new TranscriptLine(true, message.ProviderTimestamp, message.Body ?? $"[{message.MessageType}]", message.MessageType)))
            .Concat(outbound.Select(message => (
                At: message.CreatedAt,
                Order: 1,
                Id: message.OutboxMessageId,
                Line: new TranscriptLine(false, message.CreatedAt, message.Body, $"{message.Sender} · {message.DeliveryStatus}"))));

        return [.. lines.OrderBy(entry => entry.At).ThenBy(entry => entry.Order).ThenBy(entry => entry.Id).Select(entry => entry.Line)];
    }
}
