using System.Collections.Concurrent;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Stands in for the conversation orchestration of a later ticket, so the Inbox worker can be
/// exercised end to end without implementing orchestration here.
/// </summary>
internal sealed class RecordingInboundProcessor : IInboundMessageProcessor
{
    private readonly ConcurrentQueue<ClaimedInboxMessage> processed = new();

    public Exception? Failure { get; set; }

    public IReadOnlyList<ClaimedInboxMessage> Processed => [.. processed];

    public Task ProcessAsync(ClaimedInboxMessage message, CancellationToken cancellationToken = default)
    {
        processed.Enqueue(message);

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

/// <summary>
/// Stands in for the Meta transport of a later ticket, so the Outbox worker can be exercised
/// end to end without implementing the provider.
/// </summary>
internal sealed class RecordingOutboundSender : IOutboundMessageSender
{
    private readonly ConcurrentQueue<ClaimedOutboxMessage> sent = new();

    public Func<ClaimedOutboxMessage, OutboundSendResult> Result { get; set; } =
        message => OutboundSendResult.Sent($"wamid.sent.{message.Id}");

    public Exception? Failure { get; set; }

    public IReadOnlyList<ClaimedOutboxMessage> Sent => [.. sent];

    public Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        sent.Enqueue(message);

        return Failure is null
            ? Task.FromResult(Result(message))
            : Task.FromException<OutboundSendResult>(Failure);
    }
}
