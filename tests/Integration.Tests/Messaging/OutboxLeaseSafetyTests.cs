using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Workers;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// The safety property of the Outbox claim: once another worker may legally recover an expired claim,
/// the original worker can no longer act under it. The mechanism is the local lease guard, which is
/// started before the claim and bounds the claim, the Meta attempt, the accepted-delivery bookkeeping
/// and the failure bookkeeping. These tests drive the real worker with short test-only budgets, so no
/// test waits for a real lease or a real Meta request.
/// </summary>
public sealed class OutboxLeaseSafetyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Accepted_delivery_bookkeeping_is_bounded_by_one_budget_shared_by_its_retries()
    {
        var timing = Timing(bookkeeping: TimeSpan.FromMilliseconds(500), slack: TimeSpan.FromSeconds(1));
        var store = new ScriptedStore
        {
            Claim = SampleClaim(),
            TransientCompletionFailures = 2,
            BlockCompletion = true,
        };
        var sender = new ScriptedSender(OutboundSendResult.Sent("wamid.accepted"));
        await using var host = Host(store, sender, timing, lease: TimeSpan.FromMinutes(5));
        var worker = host.Services.GetRequiredService<OutboxWorker>();
        var stopwatch = Stopwatch.StartNew();

        var processed = await worker.ProcessOnceAsync().WaitAsync(TestTimeout);

        stopwatch.Stop();

        Assert.Equal(1, processed);
        Assert.Equal(1, sender.Sends);

        // The three bounded attempts still happen, but they share one budget: every attempt received
        // the same cancellation token, and the hanging last attempt ended after one test-only budget of
        // half a second instead of getting a fresh timeout of its own.
        Assert.Equal(3, store.CompletionCalls);
        Assert.Single(store.CompletionTokens.Distinct());
        Assert.All(store.CompletionTokens, token => Assert.True(token.IsCancellationRequested));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1_500),
            $"The bookkeeping took {stopwatch.Elapsed}, so the attempts did not share one budget.");

        // A successful accepted delivery is never reported as a failure.
        Assert.Equal(0, store.FailureCalls);
    }

    [Fact]
    public async Task A_claim_that_consumes_the_lease_safety_budget_starts_no_send()
    {
        var timing = Timing(bookkeeping: TimeSpan.FromSeconds(1), slack: TimeSpan.FromMilliseconds(300));
        var store = new ScriptedStore { Claim = SampleClaim(), BlockClaim = true };
        var sender = new ScriptedSender(OutboundSendResult.Sent("wamid.never"));
        await using var host = Host(store, sender, timing, lease: TimeSpan.FromSeconds(1));
        var worker = host.Services.GetRequiredService<OutboxWorker>();
        var stopwatch = Stopwatch.StartNew();

        var processed = await worker.ProcessOnceAsync().WaitAsync(TestTimeout);

        stopwatch.Stop();

        // The claim token was bounded by the lease guard, so a claim that never returns ends the poll
        // instead of leaving the worker with an apparently fresh, unlimited attempt budget.
        Assert.Equal(0, processed);
        Assert.True(store.ClaimTokenWasCancelled);
        Assert.Equal(0, sender.Sends);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The claim was not bounded by the lease guard: {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_send_cannot_outlive_the_lease_safety_deadline_and_writes_nothing_under_the_stale_claim()
    {
        var timing = Timing(bookkeeping: TimeSpan.FromSeconds(1), slack: TimeSpan.FromMilliseconds(300));
        var store = new ScriptedStore { Claim = SampleClaim() };
        var sender = new ScriptedSender(blockUntilCancelled: true);
        await using var host = Host(store, sender, timing, lease: TimeSpan.FromSeconds(1));
        var worker = host.Services.GetRequiredService<OutboxWorker>();
        var stopwatch = Stopwatch.StartNew();

        var processed = await worker.ProcessOnceAsync().WaitAsync(TestTimeout);

        stopwatch.Stop();

        Assert.Equal(1, processed);
        Assert.True(sender.SendTokenWasCancelled, "The sender was not stopped by the lease guard.");

        // The attempt ended before the database lease could be recovered, so neither a completion nor
        // a failure is written under a claim another worker may already own. Recovery records the
        // ambiguity from the lease itself.
        Assert.Equal(0, store.CompletionCalls);
        Assert.Equal(0, store.FailureCalls);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The send outlived the lease safety deadline: {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Durable_queue_commands_carry_the_explicit_finite_command_timeout()
    {
        // The connection string asks for an unbounded command timeout, which a queue statement must
        // never inherit.
        using var connection = new NpgsqlConnection(
            "Host=127.0.0.1;Port=5432;Database=unused;Username=unused;Command Timeout=0");

        using var command = MessagingQueueCommands.Create(
            connection,
            transaction: null,
            "SELECT 1",
            MessagingTimingPolicy.Default.DatabaseCommandTimeout);

        Assert.Equal(5, (int)MessagingTimingPolicy.Default.DatabaseCommandTimeout.TotalSeconds);
        Assert.Equal(5, command.CommandTimeout);
    }

    private static MessagingTimingPolicy Timing(TimeSpan bookkeeping, TimeSpan slack) =>
        new(
            CompletionBookkeepingBudget: bookkeeping,
            DatabaseCommandTimeout: TimeSpan.FromSeconds(1),
            LeaseSafetySlack: slack);

    private static ClaimedOutboxMessage SampleClaim() =>
        new(
            Id: 1,
            ConversationId: 1,
            CustomerExternalId: "20100000001",
            CorrelationId: "wamid.lease",
            Sender: "AI",
            Body: "the reply",
            ProviderMessageId: null,
            Attempts: 1,
            MaxAttempts: 5,
            DeliveryKey: "outbox:1",
            ClaimToken: Guid.NewGuid());

    /// <summary>
    /// A worker over the real module registration with the durable store replaced by a scripted one,
    /// so the lease guard can be proven without a database and without waiting for a real lease.
    /// </summary>
    private static MessagingHost Host(
        IOutboxMessageStore store,
        IOutboundMessageSender sender,
        MessagingTimingPolicy timing,
        TimeSpan lease) =>
        MessagingHost.Start(
            "Host=127.0.0.1;Port=5432;Database=unused;Username=unused",
            options =>
            {
                options.OutboxBatchSize = 1;
                options.ClaimLeaseDuration = lease;
            },
            services =>
            {
                services.AddSingleton<MessagingTimingPolicy>(timing);
                services.AddSingleton<IOutboxMessageStore>(store);
                services.AddSingleton<IOutboundMessageSender>(sender);
            });

    private sealed class ScriptedSender(
        OutboundSendResult? result = null,
        bool blockUntilCancelled = false) : IOutboundMessageSender
    {
        public int Sends { get; private set; }

        public bool SendTokenWasCancelled { get; private set; }

        public async Task<OutboundSendResult> SendAsync(
            ClaimedOutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            Sends++;

            if (!blockUntilCancelled)
            {
                return result!;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SendTokenWasCancelled = true;

                throw;
            }

            return OutboundSendResult.Sent("wamid.unreachable");
        }
    }

    private sealed class ScriptedStore : IOutboxMessageStore
    {
        public ClaimedOutboxMessage? Claim { get; init; }

        public bool BlockClaim { get; init; }

        public bool BlockCompletion { get; init; }

        /// <summary>How many leading completion attempts fail as a transient database error would.</summary>
        public int TransientCompletionFailures { get; init; }

        public int CompletionCalls { get; private set; }

        public int FailureCalls { get; private set; }

        public bool ClaimTokenWasCancelled { get; private set; }

        public List<CancellationToken> CompletionTokens { get; } = [];

        public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (Claim is null)
            {
                return [];
            }

            if (BlockClaim)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ClaimTokenWasCancelled = true;

                    throw;
                }
            }

            return [Claim];
        }

        public async Task CompleteAsync(
            long outboxMessageId,
            Guid claimToken,
            string providerMessageId,
            CancellationToken cancellationToken = default)
        {
            CompletionCalls++;
            CompletionTokens.Add(cancellationToken);

            if (CompletionCalls <= TransientCompletionFailures)
            {
                throw new InvalidOperationException("transient bookkeeping failure");
            }

            if (BlockCompletion)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        public Task<QueueFailureOutcome> FailAsync(
            long outboxMessageId,
            Guid claimToken,
            string error,
            bool terminal = false,
            TimeSpan? retryDelay = null,
            CancellationToken cancellationToken = default)
        {
            FailureCalls++;

            return Task.FromResult(QueueFailureOutcome.RetryScheduled);
        }
    }
}
