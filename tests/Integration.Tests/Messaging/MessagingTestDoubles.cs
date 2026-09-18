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

/// <summary>
/// A transport boundary that honours the sender contract: one logical delivery per delivery key. A
/// repeated send for a key the provider already accepted reconciles to that delivery and returns its
/// provider message id instead of delivering the reply a second time.
/// </summary>
internal sealed class DeliveryLedgerSender : IOutboundMessageSender
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> accepted = new(StringComparer.Ordinal);
    private int freshDeliveries;
    private int reconciliations;

    /// <summary>Deliveries the provider accepted for the first time, at most one per delivery key.</summary>
    public int FreshDeliveries
    {
        get
        {
            lock (gate)
            {
                return freshDeliveries;
            }
        }
    }

    /// <summary>Repeated sends that reconciled into a delivery the provider had already accepted.</summary>
    public int Reconciliations
    {
        get
        {
            lock (gate)
            {
                return reconciliations;
            }
        }
    }

    /// <summary>The delivery keys the transport was asked to deliver, one per logical delivery.</summary>
    public IReadOnlyCollection<string> DeliveryKeys
    {
        get
        {
            lock (gate)
            {
                return [.. accepted.Keys];
            }
        }
    }

    public string? AcceptedProviderMessageId(string deliveryKey)
    {
        lock (gate)
        {
            return accepted.GetValueOrDefault(deliveryKey);
        }
    }

    public Task<OutboundSendResult> SendAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (accepted.TryGetValue(message.DeliveryKey, out var providerMessageId))
            {
                reconciliations++;

                return Task.FromResult(OutboundSendResult.Sent(providerMessageId));
            }

            var acceptedProviderMessageId = $"wamid.{message.DeliveryKey}";

            accepted[message.DeliveryKey] = acceptedProviderMessageId;
            freshDeliveries++;

            return Task.FromResult(OutboundSendResult.Sent(acceptedProviderMessageId));
        }
    }
}
