using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

/// <summary>
/// Drains the durable Outbox queue. It only claims work when a sender is registered, so the
/// application runs normally before the Meta transport exists and nothing is stranded.
/// </summary>
public sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly MessagingQueueOptions options;
    private readonly MessagingTimingPolicy timing;
    private readonly ILogger<OutboxWorker> logger;

    /// <summary>
    /// The timing policy stays module-internal, so the worker is composed by the module's own
    /// registration instead of by a public constructor signature.
    /// </summary>
    internal OutboxWorker(
        IServiceScopeFactory scopeFactory,
        MessagingQueueOptions options,
        MessagingTimingPolicy timing,
        ILogger<OutboxWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.options = options;
        this.timing = timing;
        this.logger = logger;
    }

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
            // The hard lease guard starts here, immediately before the claim. The database lease
            // cannot start earlier than this moment, so this local deadline always expires before the
            // lease can be recovered, and every step below - claim, send, completion, failure - is
            // stopped by it. The guard is relative elapsed time, never a clock comparison with
            // PostgreSQL, so it needs no synchronization with the database clock.
            using var lease = new CancellationTokenSource();
            lease.CancelAfter(timing.LeaseSafetyDeadline(options.ClaimLeaseDuration));

            // The claim path is several sequential database operations (connection, transaction, the
            // retry requeue, the expired-claim recovery, the claim statement and the commit), so it gets
            // its own overall budget as well as the per-statement command timeout. This budget applies
            // only here: once the claim returns, the send and its bookkeeping keep their own budgets.
            using var claimBudget = new CancellationTokenSource();
            claimBudget.CancelAfter(timing.ClaimBudget);

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lease.Token,
                claimBudget.Token);

            IReadOnlyList<ClaimedOutboxMessage> claimed;

            try
            {
                claimed = await store.ClaimAsync(1, attempt.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Cancellation is observed at the client, not necessarily inside the database, so the
                // claim may already have committed before this token fired. Either way no provider send
                // starts here and no provider outcome is invented: a claim that did not commit leaves the
                // message claimable by a later poll, and a claim that committed stays held until its lease
                // expires, where the existing lease recovery handles that row.
                if (claimBudget.IsCancellationRequested)
                {
                    logger.LogWarning(
                        "The Outbox poll stopped before claiming: the claim exceeded its configured "
                        + "overall claim budget of {ClaimBudget}, so no provider send started. A claim "
                        + "that did not commit stays available; one that committed before this "
                        + "cancellation was observed is recovered by its lease.",
                        timing.ClaimBudget);
                }
                else
                {
                    logger.LogWarning(
                        "The Outbox poll stopped before claiming: the claim did not finish inside the "
                        + "lease safety deadline, so no provider send started. A claim that did not "
                        + "commit stays available; one that committed before this cancellation was "
                        + "observed is recovered by its lease.");
                }

                break;
            }

            if (claimed.Count == 0)
            {
                break;
            }

            await SendAsync(store, sender, claimed[0], lease.Token, cancellationToken);
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
        CancellationToken leaseToken,
        CancellationToken hostToken)
    {
        OutboundSendResult result;

        // The attempt ends at whichever comes first: host shutdown, the configured Meta timeout
        // inside the sender, or this claim's lease-safety deadline.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(hostToken, leaseToken);

        try
        {
            result = await sender.SendAsync(message, attempt.Token);
        }
        catch (OperationCanceledException) when (hostToken.IsCancellationRequested)
        {
            // Shutdown is not a delivery failure: the claim stays held until its lease expires.
            throw;
        }
        catch (OperationCanceledException) when (leaseToken.IsCancellationRequested)
        {
            // The lease guard ended the attempt before the database lease became recoverable. A
            // request that may already have left the process has an unknown outcome, so nothing is
            // written under a claim that is about to be recoverable: the durable lease recovery
            // records the ambiguity instead.
            logger.LogWarning(
                "The lease safety deadline of Outbox message {OutboxMessageId} ended the attempt before "
                + "it reported an outcome. The provider outcome is unknown and the claim lease recovers "
                + "the message.",
                message.Id);

            return;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(
                store,
                message,
                MessagingDiagnostics.UnexpectedOutboundFailure(exception),
                leaseToken,
                exception);

            return;
        }

        if (result.Outcome == OutboundSendOutcome.Accepted)
        {
            // The provider accepted the delivery, so this attempt has left the transport. Only the
            // bookkeeping of the accepted delivery is left, and a failure there is never evidence that
            // the provider refused the message: reporting it as a failed send would deliver twice.
            await RecordAcceptedDeliveryAsync(store, message, result.ProviderMessageId!, leaseToken);

            return;
        }

        await RecordFailureAsync(
            store,
            message,
            result.Error ?? ErrorFor(result.Outcome),
            leaseToken,
            terminal: result.Outcome == OutboundSendOutcome.PermanentFailure,
            retryDelay: result.RetryAfter);
    }

    private async Task RecordAcceptedDeliveryAsync(
        IOutboxMessageStore store,
        ClaimedOutboxMessage message,
        string providerMessageId,
        CancellationToken leaseToken)
    {
        // A known provider acceptance must get a chance to be recorded even while the host is shutting
        // down, so this token is deliberately not linked to host cancellation. It is still bounded:
        // it honours the hard lease-safety deadline and one overall completion budget that every
        // attempt below shares, so the bookkeeping can never outlive the claim it is recording.
        using var bookkeeping = CancellationTokenSource.CreateLinkedTokenSource(leaseToken);
        bookkeeping.CancelAfter(timing.CompletionBookkeepingBudget);

        for (var attempt = 1; attempt <= CompletionBookkeepingAttempts; attempt++)
        {
            try
            {
                await store.CompleteAsync(message.Id, message.ClaimToken, providerMessageId, bookkeeping.Token);

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
            catch (OperationCanceledException) when (bookkeeping.Token.IsCancellationRequested)
            {
                // The budget or the lease-safety deadline ended the bookkeeping. The delivery is not
                // sent again by this worker: its claim lease recovers the message, and a later retry
                // may no longer be able to prove that Meta already accepted it.
                logger.LogWarning(
                    "The completion bookkeeping budget of Outbox message {OutboxMessageId} ended before "
                    + "the accepted delivery could be recorded. The message is not sent again here; its "
                    + "claim lease recovers it, and a later retry may duplicate externally because the "
                    + "provider acceptance could not be stored.",
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
        CancellationToken leaseToken,
        Exception? exception = null,
        bool terminal = false,
        TimeSpan? retryDelay = null)
    {
        QueueFailureOutcome outcome;

        try
        {
            // The failure is written under the same lease-safety deadline as the attempt it reports:
            // an expired guard must not let this worker mutate a claim a newer owner may hold now.
            outcome = await store.FailAsync(
                message.Id,
                message.ClaimToken,
                error,
                terminal,
                retryDelay,
                leaseToken);
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
        catch (OperationCanceledException) when (leaseToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "The lease safety deadline of Outbox message {OutboxMessageId} ended before its failure "
                + "could be recorded. The claim lease recovers the message.",
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
