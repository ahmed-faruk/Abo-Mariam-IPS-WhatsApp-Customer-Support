using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

/// <summary>
/// Drains the durable Inbox queue. It only claims work when a processor is registered, so the
/// application runs normally before conversation orchestration exists and nothing is stranded.
/// </summary>
public sealed class InboxWorker(
    IServiceScopeFactory scopeFactory,
    MessagingQueueOptions options,
    ILogger<InboxWorker> logger) : BackgroundService
{
    public const string MissingProcessorMessage =
        "No IInboundMessageProcessor is registered, so the Inbox worker stays idle. "
        + "Inbound messages are durable and remain unclaimed.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var probe = scopeFactory.CreateScope())
        {
            if (probe.ServiceProvider.GetService<IInboundMessageProcessor>() is null)
            {
                logger.LogInformation("Inbox worker idle: {Reason}", MissingProcessorMessage);

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
    /// Runs one bounded poll: claim a batch, process every claimed message, record every outcome.
    /// The hosted service is the only production caller; the persistence suite drives a single poll
    /// directly instead of racing its own polling loop.
    /// </summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IInboundMessageProcessor>() is null)
        {
            return 0;
        }

        var processor = scope.ServiceProvider.GetRequiredService<IInboundMessageProcessor>();
        var store = scope.ServiceProvider.GetRequiredService<IInboxMessageStore>();
        var claimed = await store.ClaimAsync(options.InboxBatchSize, cancellationToken);

        foreach (var message in claimed)
        {
            await ProcessAsync(store, processor, message, cancellationToken);
        }

        return claimed.Count;
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
            logger.LogError(exception, "The Inbox worker poll failed. The work stays durable.");

            // Back off instead of spinning through a persistent failure such as a lost connection.
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

    private async Task ProcessAsync(
        IInboxMessageStore store,
        IInboundMessageProcessor processor,
        ClaimedInboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await processor.ProcessAsync(message, cancellationToken);
            await store.CompleteAsync(message.Id, message.ClaimToken, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown is not a business failure: the claim stays held until its lease expires.
            throw;
        }
        catch (ClaimOwnershipLostException exception)
        {
            // The lease expired and another worker owns this message now. This worker must not
            // overwrite that owner's bookkeeping, and there is nothing left for it to record.
            logger.LogWarning(
                exception,
                "Inbox message {InboxMessageId} is no longer claimed by this worker, so no outcome was recorded.",
                message.Id);
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(store, message, exception, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        IInboxMessageStore store,
        ClaimedInboxMessage message,
        Exception failure,
        CancellationToken cancellationToken)
    {
        QueueFailureOutcome outcome;

        try
        {
            outcome = await store.FailAsync(message.Id, message.ClaimToken, failure.Message, cancellationToken);
        }
        catch (ClaimOwnershipLostException exception)
        {
            logger.LogWarning(
                exception,
                "Inbox message {InboxMessageId} is no longer claimed by this worker, so its failure was not recorded.",
                message.Id);

            return;
        }

        logger.LogWarning(
            failure,
            "Inbox message {InboxMessageId} failed on attempt {Attempts}: {Outcome}.",
            message.Id,
            message.Attempts,
            outcome);
    }
}
