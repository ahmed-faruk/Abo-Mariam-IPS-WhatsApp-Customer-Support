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
    /// Runs one bounded poll: claim a batch, send every claimed message, record every outcome.
    /// </summary>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IOutboundMessageSender>() is null)
        {
            return 0;
        }

        var sender = scope.ServiceProvider.GetRequiredService<IOutboundMessageSender>();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
        var claimed = await store.ClaimAsync(options.OutboxBatchSize, cancellationToken);

        foreach (var message in claimed)
        {
            await SendAsync(store, sender, message, cancellationToken);
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
        try
        {
            var result = await sender.SendAsync(message, cancellationToken);

            if (result.Succeeded)
            {
                await store.CompleteAsync(message.Id, result.ProviderMessageId!, cancellationToken);

                return;
            }

            await RecordFailureAsync(store, message, result.Error ?? "The provider refused the send.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown is not a delivery failure: the claim stays held rather than being reported.
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(store, message, exception.Message);
        }
    }

    private async Task RecordFailureAsync(
        IOutboxMessageStore store,
        ClaimedOutboxMessage message,
        string error)
    {
        var outcome = await store.FailAsync(message.Id, error, CancellationToken.None);

        logger.LogWarning(
            "Outbox message {OutboxMessageId} failed on attempt {Attempts} of {MaxAttempts}: {Outcome} ({Error}).",
            message.Id,
            message.Attempts,
            message.MaxAttempts,
            outcome,
            error);
    }
}
