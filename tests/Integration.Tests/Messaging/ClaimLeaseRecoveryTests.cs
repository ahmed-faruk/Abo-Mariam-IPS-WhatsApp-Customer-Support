using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 1: a claim is a lease. Work abandoned by a worker that crashed, was cancelled or lost its
/// database becomes claimable again once the lease expires, and the owner that lost the lease can
/// never record an outcome for the claim that somebody else holds now.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClaimLeaseRecoveryTests(PostgresContainerFixture postgres) : MessagingQueueFixture(postgres)
{
    public static TheoryData<QueueKind> Queues => new() { QueueKind.Inbox, QueueKind.Outbox };

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task An_abandoned_claim_is_recovered_only_after_its_lease_expires(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;
        long otherPartitionId;

        await using (var scope = host.CreateScope())
        {
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002001", "lease-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002001", "lease-2");
            otherPartitionId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002002", "lease-3");
        }

        QueueClaim abandoned;

        await using (var scope = host.CreateScope())
        {
            var claimed = await ClaimAsync(queue, scope.ServiceProvider, 10);

            // The oldest message of each partition is claimed: a lease serializes its own partition
            // and never the queue, so the unrelated partition moves in the same claim.
            Assert.Equal(new[] { firstId, otherPartitionId }, claimed.Select(claim => claim.Id));
            Assert.Equal(1, claimed[0].Attempts);

            abandoned = claimed[0];
        }

        await using (var scope = host.CreateScope())
        {
            // The lease is still running, so nothing is recoverable yet and the later message of the
            // abandoned partition stays exactly where the documentation puts it: unclaimed.
            Assert.Equal("true", await LeaseIsRunningAsync(queue, firstId));
            Assert.Empty(await ClaimAsync(queue, scope.ServiceProvider, 10));
            Assert.Equal("Claimed", await StatusAsync(queue, firstId));
            Assert.Equal("1", await AttemptsAsync(queue, firstId));
            Assert.Equal("Pending", await StatusAsync(queue, secondId));
            Assert.Equal("0", await AttemptsAsync(queue, secondId));
        }

        // Only the database clock ends a lease, and the expired claim becomes recoverable.
        await ExpireClaimAsync(queue, firstId);

        await using (var scope = host.CreateScope())
        {
            var recovered = Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10));

            Assert.Equal(firstId, recovered.Id);
            Assert.NotEqual(abandoned.ClaimToken, recovered.ClaimToken);
            Assert.Equal(2, recovered.Attempts);
            Assert.Equal("Pending", await StatusAsync(queue, secondId));

            // The recovered message still owns its partition until it is completed, and only then
            // does the later message of that partition become claimable.
            await CompleteAsync(queue, scope.ServiceProvider, recovered);

            Assert.Equal(TerminalStatus(queue), await StatusAsync(queue, firstId));
            Assert.Equal(secondId, Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10)).Id);
        }
    }

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task A_stale_claim_owner_cannot_complete_or_fail_the_recovered_claim(QueueKind queue)
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long firstId;
        long secondId;

        await using (var scope = host.CreateScope())
        {
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002101", "stale-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002101", "stale-2");
        }

        QueueClaim abandoned;
        QueueClaim recovered;

        await using (var scope = host.CreateScope())
        {
            abandoned = Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10));
        }

        await ExpireClaimAsync(queue, firstId);

        await using (var scope = host.CreateScope())
        {
            recovered = Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10));

            Assert.Equal(2, recovered.Attempts);
        }

        await using (var scope = host.CreateScope())
        {
            var services = scope.ServiceProvider;

            // The owner of the expired lease is fenced out of both outcomes of the message it no
            // longer holds, so it can never overwrite the bookkeeping of the newer owner.
            await Assert.ThrowsAsync<ClaimOwnershipLostException>(
                () => CompleteWithTokenAsync(queue, services, firstId, abandoned.ClaimToken));
            await Assert.ThrowsAsync<ClaimOwnershipLostException>(
                () => FailWithTokenAsync(queue, services, firstId, abandoned.ClaimToken, "stale owner"));

            Assert.Equal("Claimed", await StatusAsync(queue, firstId));
            Assert.Equal("2", await AttemptsAsync(queue, firstId));
            // A recovered Outbox attempt is recorded as an uncertain provider outcome, because its
            // transport may already have run. The Inbox holds no provider outcome, so it stays clean.
            Assert.Equal(
                queue == QueueKind.Outbox ? MessagingDiagnostics.ExpiredClaimRecovered : string.Empty,
                await LastErrorAsync(queue, firstId));
            Assert.Equal(recovered.ClaimToken, Guid.Parse(await ClaimTokenAsync(queue, firstId)));
            Assert.Equal("Pending", await StatusAsync(queue, secondId));

            // The owner of the recovered claim records the outcome instead.
            await CompleteWithTokenAsync(queue, services, firstId, recovered.ClaimToken);

            Assert.Equal(TerminalStatus(queue), await StatusAsync(queue, firstId));
            Assert.Equal(string.Empty, await ClaimTokenAsync(queue, firstId));
            Assert.Equal(secondId, Assert.Single(await ClaimAsync(queue, services, 10)).Id);
        }
    }

    [Theory]
    [MemberData(nameof(Queues))]
    public async Task The_owner_of_a_recovered_claim_records_the_failure_and_the_attempt_limit_still_applies(
        QueueKind queue)
    {
        const int attemptLimit = 3;

        await using var host = MessagingHost.Start(
            ConnectionString,
            options =>
            {
                options.InboxMaxAttempts = attemptLimit;
                options.InboxRetryDelay = TimeSpan.FromHours(1);
                options.OutboxRetryDelay = TimeSpan.FromHours(1);
            });

        long firstId;
        long secondId;

        await using (var scope = host.CreateScope())
        {
            firstId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002201", "recovered-1");
            secondId = await EnqueueAsync(queue, scope.ServiceProvider, "20100002201", "recovered-2");
        }

        await AlignAttemptLimitAsync(queue, firstId, attemptLimit);

        await using (var scope = host.CreateScope())
        {
            Assert.Single(await ClaimAsync(queue, scope.ServiceProvider, 10));
        }

        await ExpireClaimAsync(queue, firstId);

        await using (var scope = host.CreateScope())
        {
            var services = scope.ServiceProvider;
            var recovered = Assert.Single(await ClaimAsync(queue, services, 10));

            Assert.Equal(2, recovered.Attempts);

            // The recovered claim records a failure, and the retry rules still apply to it.
            Assert.Equal(
                QueueFailureOutcome.RetryScheduled,
                await FailAsync(queue, services, recovered, "the recovered attempt failed"));

            Assert.Equal("Failed", await StatusAsync(queue, firstId));
            Assert.Equal("the recovered attempt failed", await LastErrorAsync(queue, firstId));
            Assert.Equal("2", await AttemptsAsync(queue, firstId));
            Assert.Equal("1", await Catalog.ScalarAsync(
                $"SELECT count(*) FROM {Table(queue)} WHERE id = {firstId} AND run_after > now()"));

            // The failed message owns the partition until its retry is due, so nothing overtakes it.
            Assert.Empty(await ClaimAsync(queue, services, 10));
            Assert.Equal("Pending", await StatusAsync(queue, secondId));
            Assert.Equal("0", await AttemptsAsync(queue, secondId));

            await MakeDueAsync(queue, firstId);

            var retried = Assert.Single(await ClaimAsync(queue, services, 10));

            Assert.Equal(firstId, retried.Id);
            Assert.Equal(3, retried.Attempts);

            // The attempt limit still dead-letters, and a terminal message stops blocking.
            Assert.Equal(
                QueueFailureOutcome.DeadLettered,
                await FailAsync(queue, services, retried, "the retry failed too"));

            Assert.Equal("DeadLettered", await StatusAsync(queue, firstId));
            Assert.Equal(secondId, Assert.Single(await ClaimAsync(queue, services, 10)).Id);
        }
    }

    [Fact]
    public async Task An_expired_outbox_claim_at_the_attempt_limit_dead_letters_instead_of_requeueing_forever()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long id;

        await using (var scope = host.CreateScope())
        {
            id = await EnqueueAsync(QueueKind.Outbox, scope.ServiceProvider, "20100002301", "expired-max");
        }

        await AlignAttemptLimitAsync(QueueKind.Outbox, id, attemptLimit: 1);

        await using (var scope = host.CreateScope())
        {
            var claimed = Assert.Single(await ClaimAsync(QueueKind.Outbox, scope.ServiceProvider, 10));

            Assert.Equal(1, claimed.Attempts);
        }

        await ExpireClaimAsync(QueueKind.Outbox, id);

        await using (var scope = host.CreateScope())
        {
            Assert.Empty(await ClaimAsync(QueueKind.Outbox, scope.ServiceProvider, 10));
        }

        Assert.Equal("DeadLettered", await OutboxStatusAsync(id));
        Assert.Equal(
            MessagingDiagnostics.ExpiredClaimExhausted,
            await LastErrorAsync(QueueKind.Outbox, id));
    }

    [Fact]
    public async Task An_expired_outbox_claim_below_the_attempt_limit_recovers_as_an_unknown_outcome()
    {
        await using var host = MessagingHost.Start(ConnectionString);

        long id;

        await using (var scope = host.CreateScope())
        {
            id = await EnqueueAsync(QueueKind.Outbox, scope.ServiceProvider, "20100002501", "expired-below");
        }

        QueueClaim abandoned;

        await using (var scope = host.CreateScope())
        {
            abandoned = Assert.Single(await ClaimAsync(QueueKind.Outbox, scope.ServiceProvider, 10));

            Assert.Equal(1, abandoned.Attempts);
        }

        await ExpireClaimAsync(QueueKind.Outbox, id);

        await using (var scope = host.CreateScope())
        {
            var recovered = Assert.Single(await ClaimAsync(QueueKind.Outbox, scope.ServiceProvider, 10));

            // The abandoned attempt may already have left the transport, so the recovery is recorded
            // as uncertain and can never be read later as a provider refusal.
            Assert.Equal(id, recovered.Id);
            Assert.NotEqual(abandoned.ClaimToken, recovered.ClaimToken);
            Assert.Equal(2, recovered.Attempts);
            Assert.Equal(MessagingDiagnostics.ExpiredClaimRecovered, await LastErrorAsync(QueueKind.Outbox, id));

            // The stale owner is still fenced out of the claim it lost.
            await Assert.ThrowsAsync<ClaimOwnershipLostException>(
                () => CompleteWithTokenAsync(QueueKind.Outbox, scope.ServiceProvider, id, abandoned.ClaimToken));

            Assert.Equal("Claimed", await OutboxStatusAsync(id));
            Assert.Equal(recovered.ClaimToken, Guid.Parse(await ClaimTokenAsync(QueueKind.Outbox, id)));
        }
    }

    /// <summary>
    /// The Inbox attempt limit is queue policy while the Outbox limit is stored per message, so the
    /// stored Outbox limit is aligned with the configured Inbox limit for the shared assertion.
    /// </summary>
    private Task AlignAttemptLimitAsync(QueueKind queue, long id, int attemptLimit) =>
        queue == QueueKind.Outbox
            ? Catalog.ExecuteAsync($"UPDATE messaging.outbox_message SET max_attempts = {attemptLimit} WHERE id = {id}")
            : Task.CompletedTask;
}
