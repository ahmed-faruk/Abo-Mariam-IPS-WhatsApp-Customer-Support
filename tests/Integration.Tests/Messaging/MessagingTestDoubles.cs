using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Stands in for the conversation orchestration of a later ticket, so the Inbox worker can be
/// exercised end to end without implementing orchestration here. Everything the worker does with the
/// message is asserted from the durable queue, so this stub only decides whether processing succeeds.
/// </summary>
internal sealed class StubInboundProcessor : IInboundMessageProcessor
{
    public Exception? Failure { get; set; }

    public Task ProcessAsync(ClaimedInboxMessage message, CancellationToken cancellationToken = default) =>
        Failure is null ? Task.CompletedTask : Task.FromException(Failure);
}

/// <summary>
/// Stands in for the Meta transport of a later ticket, so the Outbox worker can be exercised end to
/// end without implementing the provider.
/// </summary>
internal sealed class StubOutboundSender : IOutboundMessageSender
{
    public Func<ClaimedOutboxMessage, OutboundSendResult> Result { get; set; } =
        message => OutboundSendResult.Sent($"wamid.sent.{message.Id}");

    public Exception? Failure { get; set; }

    public Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default) =>
        Failure is null
            ? Task.FromResult(Result(message))
            : Task.FromException<OutboundSendResult>(Failure);
}
