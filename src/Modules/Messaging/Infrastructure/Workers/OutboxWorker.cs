using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

/// <summary>
/// Drains the durable Outbox queue. It only claims work when a sender is registered, so the
/// application runs normally before the Meta transport exists and nothing is stranded.
/// </summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    MessagingQueueOptions options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often the accepted delivery of a successful send is offered to the Outbox store again
    /// before the worker gives up and lets the claim lease recover it.
    /// </summary>
    private const int CompletionBookkeepingAttempts = 3;

    public const string MissingSenderMessage =
        "No IOutboundMessageSender is registered, so the Outbox worker stays idle. "
        + "Outbound intents are durable and remain unclaimed.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var probe = scopeFactory.CreateScope())
        {
            if (probe.ServiceProvider.GetService<IOutboundMessageSender>() is null)
            {
                logger.LogInformation("Outbox worker idle: {Reason}", MissingSenderMessage);

                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = await PollAsync(stoppingToken);

            if (claimed == 0 && !await SleepAsync(stoppingToken))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one bounded poll: claim one due message, send it, record its outcome, and repeat while the
    /// batch budget lasts. Claiming immediately before the send keeps the lease that authorizes an
    /// external attempt fresh, so no send can ever begin on a lease that expired while an earlier
    /// reply of the same poll was still in flight and that another worker could already have
    /// reclaimed. The hosted service is the only production caller; the persistence suite drives a
    /// single poll directly instead of racing its own polling loop.
    /// </summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IOutboundMessageSender>() is null)
        {
            return 0;
        }

        var sender = scope.ServiceProvider.GetRequiredService<IOutboundMessageSender>();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
        var processed = 0;

        while (processed < options.OutboxBatchSize)
        {
            var claimed = await store.ClaimAsync(1, cancellationToken);

            if (claimed.Count == 0)
            {
                break;
            }

            await SendAsync(store, sender, claimed[0], cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task<int> PollAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await ProcessOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The Outbox worker poll failed. The work stays durable.");

            return 0;
        }
    }

    private async Task<bool> SleepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.IdlePollDelay, stoppingToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SendAsync(
        IOutboxMessageStore store,
        IOutboundMessageSender sender,
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken)
    {
        OutboundSendResult result;

        try
        {
            result = await sender.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown is not a delivery failure: the claim stays held until its lease expires.
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(
                store,
                message,
                MessagingDiagnostics.UnexpectedOutboundFailure(exception),
                exception);

            return;
        }

        if (result.Outcome == OutboundSendOutcome.Accepted)
        {
            // The provider accepted the delivery, so this attempt has left the transport. Only the
            // bookkeeping of the accepted delivery is left, and a failure there is never evidence that
            // the provider refused the message: reporting it as a failed send would deliver twice.
            await RecordAcceptedDeliveryAsync(store, message, result.ProviderMessageId!);

            return;
        }

        await RecordFailureAsync(
            store,
            message,
            result.Error ?? ErrorFor(result.Outcome),
            terminal: result.Outcome == OutboundSendOutcome.PermanentFailure,
            retryDelay: result.RetryAfter);
    }

    private async Task RecordAcceptedDeliveryAsync(
        IOutboxMessageStore store,
        ClaimedOutboxMessage message,
        string providerMessageId)
    {
        for (var attempt = 1; attempt <= CompletionBookkeepingAttempts; attempt++)
        {
            try
            {
                // The durable record of an accepted delivery must not be abandoned by a shutdown, so
                // the completion is written with no cancellation token.
                await store.CompleteAsync(message.Id, message.ClaimToken, providerMessageId, CancellationToken.None);

                return;
            }
            catch (ClaimOwnershipLostException exception)
            {
                logger.LogWarning(
                    exception,
                    "Outbox message {OutboxMessageId} is no longer claimed by this worker, so the accepted "
                    + "delivery was not recorded. A later retry may not be able to prove whether the "
                    + "provider already accepted this delivery.",
                    message.Id);

                return;
            }
            catch (Exception exception) when (attempt < CompletionBookkeepingAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Recording the accepted delivery of Outbox message {OutboxMessageId} failed. Retrying "
                    + "the bookkeeping instead of the send.",
                    message.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The accepted delivery of Outbox message {OutboxMessageId} could not be recorded. Its "
                    + "claim lease recovers the message, but the previous provider outcome is now "
                    + "ambiguous and a later retry may duplicate externally.",
                    message.Id);
            }
        }
    }

    private async Task RecordFailureAsync(
        IOutboxMessageStore store,
        ClaimedOutboxMessage message,
        string error,
        Exception? exception = null,
        bool terminal = false,
        TimeSpan? retryDelay = null)
    {
        QueueFailureOutcome outcome;

        try
        {
            outcome = await store.FailAsync(
                message.Id,
                message.ClaimToken,
                error,
                terminal,
                retryDelay,
                CancellationToken.None);
        }
        catch (ClaimOwnershipLostException lostClaim)
        {
            logger.LogWarning(
                lostClaim,
                "Outbox message {OutboxMessageId} is no longer claimed by this worker, so its failure "
                + "was not recorded.",
                message.Id);

            return;
        }

        // The durable text stays a stable bounded classification, while the exception itself is only
        // ever logged, so an operator keeps the detail without an uncontrolled string in the queue.
        logger.LogWarning(
            exception,
            "Outbox message {OutboxMessageId} failed on attempt {Attempts} of {MaxAttempts}: {Outcome} ({Error}).",
            message.Id,
            message.Attempts,
            message.MaxAttempts,
            outcome,
            error);
    }

    private static string ErrorFor(OutboundSendOutcome outcome) =>
        outcome switch
        {
            OutboundSendOutcome.RetryableFailure => "The provider reported a retryable failure.",
            OutboundSendOutcome.PermanentFailure => "The provider reported a permanent failure.",
            OutboundSendOutcome.Unknown => MessagingDiagnostics.AcceptanceNotEstablished,
            _ => "The outbound send did not complete.",
        };
}
