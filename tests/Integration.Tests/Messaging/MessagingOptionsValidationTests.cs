using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// Finding 11: an invalid queue policy is rejected before a worker begins polling, instead of making
/// every poll throw while the durable queue stays untouched.
/// </summary>
public sealed class MessagingOptionsValidationTests
{
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=5432;Database=messaging_options;Username=monitor_app";

    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The enforced budgets of one claimed item that are not the provider attempt, taken from the same
    /// policy the workers run with rather than restated here.
    /// </summary>
    private static TimeSpan DefaultNonProviderBudget =>
        MessagingTimingPolicy.Default.ClaimBudget
        + MessagingTimingPolicy.Default.CompletionBookkeepingBudget
        + MessagingTimingPolicy.Default.LeaseSafetySlack;

    /// <summary>
    /// The largest provider timeout whose whole enforced sequence still fits strictly inside the default
    /// lease: the boundary itself is rejected, so the largest accepted value is one second below it.
    /// </summary>
    private static int LargestTimeoutInsideTheDefaultLease =>
        (int)(DefaultLease - DefaultNonProviderBudget).TotalSeconds - 1;

    [Fact]
    public void Every_invalid_queue_setting_is_rejected_and_named()
    {
        (string Setting, Action<MessagingQueueOptions> Configure)[] invalid =
        [
            (nameof(MessagingQueueOptions.InboxBatchSize), options => options.InboxBatchSize = 0),
            (nameof(MessagingQueueOptions.InboxBatchSize), options => options.InboxBatchSize = -1),
            (nameof(MessagingQueueOptions.OutboxBatchSize), options => options.OutboxBatchSize = 0),
            (nameof(MessagingQueueOptions.OutboxBatchSize), options => options.OutboxBatchSize = -4),
            (nameof(MessagingQueueOptions.InboxMaxAttempts), options => options.InboxMaxAttempts = 0),
            (nameof(MessagingQueueOptions.InboxMaxAttempts), options => options.InboxMaxAttempts = -1),
            (nameof(MessagingQueueOptions.InboxRetryDelay), options => options.InboxRetryDelay = TimeSpan.Zero),
            (nameof(MessagingQueueOptions.InboxRetryDelay), options => options.InboxRetryDelay = TimeSpan.FromSeconds(-1)),
            (nameof(MessagingQueueOptions.OutboxRetryDelay), options => options.OutboxRetryDelay = TimeSpan.Zero),
            (nameof(MessagingQueueOptions.OutboxRetryDelay), options => options.OutboxRetryDelay = TimeSpan.FromSeconds(-1)),
            (nameof(MessagingQueueOptions.OutboxMaxRetryDelay), options => options.OutboxMaxRetryDelay = TimeSpan.Zero),
            (nameof(MessagingQueueOptions.OutboxMaxRetryDelay), options => options.OutboxMaxRetryDelay = TimeSpan.FromSeconds(-1)),
            (nameof(MessagingQueueOptions.OutboxRetryDelay), options => options.OutboxRetryDelay = TimeSpan.FromHours(2)),
            (nameof(MessagingQueueOptions.ClaimLeaseDuration), options => options.ClaimLeaseDuration = TimeSpan.Zero),
            (nameof(MessagingQueueOptions.ClaimLeaseDuration), options => options.ClaimLeaseDuration = TimeSpan.FromSeconds(-1)),
            // The lease guard is the claim lease minus the lease-safety slack, so a lease that short
            // leaves no guard at all and is rejected instead of running without one.
            (nameof(MessagingQueueOptions.ClaimLeaseDuration), options => options.ClaimLeaseDuration = TimeSpan.FromSeconds(5)),
            // The guard is a scheduled cancellation, so a lease beyond the schedulable ceiling cannot
            // produce a guard either.
            (nameof(MessagingQueueOptions.ClaimLeaseDuration), options => options.ClaimLeaseDuration = TimeSpan.FromDays(60)),
            (nameof(MessagingQueueOptions.IdlePollDelay), options => options.IdlePollDelay = TimeSpan.Zero),
            (nameof(MessagingQueueOptions.IdlePollDelay), options => options.IdlePollDelay = TimeSpan.FromSeconds(-1)),
        ];

        foreach (var (setting, configure) in invalid)
        {
            using var provider = BuildProvider(configure);

            var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveQueueOptions(provider));

            Assert.True(
                exception.Message.Contains(setting, StringComparison.Ordinal),
                $"The rejected setting '{setting}' was not named in: {exception.Message}");
        }
    }

    [Fact]
    public void The_documented_default_policy_is_accepted()
    {
        using var provider = BuildProvider(_ => { });

        var options = ResolveQueueOptions(provider);

        Assert.True(options.InboxBatchSize > 0);
        Assert.True(options.OutboxBatchSize > 0);
        Assert.True(options.InboxMaxAttempts > 0);
        Assert.True(options.InboxRetryDelay > TimeSpan.Zero);
        Assert.True(options.OutboxRetryDelay > TimeSpan.Zero);
        Assert.True(options.ClaimLeaseDuration > TimeSpan.Zero);
        Assert.True(options.IdlePollDelay > TimeSpan.Zero);
    }

    [Fact]
    public async Task A_host_with_an_invalid_queue_setting_fails_before_its_workers_start()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddMessagingModule(
            UnusedConnectionString,
            options => options.ClaimLeaseDuration = TimeSpan.Zero);

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(
            nameof(MessagingQueueOptions.ClaimLeaseDuration),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_transport_budget_fits_inside_the_default_claim_lease()
    {
        using var provider = BuildTransportProvider(_ => { }, _ => { });

        var options = ResolveWhatsAppOptions(provider);

        Assert.True(options.TimeoutSeconds > 0);
    }

    /// <summary>
    /// With the default five-minute lease the worker needs <see cref="DefaultNonProviderBudget"/> of
    /// enforced budget that is not the provider attempt: the bounded overall claim path, the shared
    /// completion-bookkeeping budget and the lease-safety slack. A provider timeout at or above 240
    /// seconds therefore cannot fit; the exact boundary is derived in
    /// <see cref="The_transport_budget_boundary_is_exclusive_on_the_required_claim_lease"/>.
    /// </summary>
    [Theory]
    [InlineData(240)]
    [InlineData(265)]
    [InlineData(270)]
    [InlineData(300)]
    [InlineData(301)]
    public void A_transport_budget_that_cannot_finish_inside_the_claim_lease_is_rejected(int timeoutSeconds)
    {
        using var provider = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = TimeSpan.FromMinutes(5),
            whatsApp => whatsApp.TimeoutSeconds = timeoutSeconds);

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveWhatsAppOptions(provider));

        Assert.Contains(nameof(WhatsAppOptions.TimeoutSeconds), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MessagingQueueOptions.ClaimLeaseDuration), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transport_budget_with_room_for_its_bookkeeping_margin_is_accepted()
    {
        using var provider = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = DefaultLease,
            whatsApp => whatsApp.TimeoutSeconds = LargestTimeoutInsideTheDefaultLease);

        Assert.Equal(LargestTimeoutInsideTheDefaultLease, ResolveWhatsAppOptions(provider).TimeoutSeconds);
    }

    /// <summary>
    /// The validator's boundary is strictly greater than, so a lease exactly equal to the whole enforced
    /// sequence is rejected and one tick more is the smallest lease that fits.
    /// </summary>
    [Fact]
    public void The_transport_budget_boundary_is_exclusive_on_the_required_claim_lease()
    {
        var required = MessagingTimingPolicy.Default.RequiredClaimLease(providerTimeoutSeconds: 20);

        // Independently stated total of the enforced sequence: claim path 30s, provider attempt 20s,
        // completion bookkeeping 20s and lease-safety slack 10s. It fails if the claim term silently
        // goes back to the per-statement command timeout (5s).
        Assert.Equal(TimeSpan.FromSeconds(80), required);

        using var rejected = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = required,
            _ => { });

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveWhatsAppOptions(rejected));

        Assert.Contains(nameof(WhatsAppOptions.TimeoutSeconds), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MessagingQueueOptions.ClaimLeaseDuration), exception.Message, StringComparison.Ordinal);

        using var accepted = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = required + TimeSpan.FromTicks(1),
            _ => { });

        Assert.Equal(20, ResolveWhatsAppOptions(accepted).TimeoutSeconds);
    }

    [Fact]
    public void The_queue_lease_bound_is_exclusive_at_the_lease_safety_slack()
    {
        // The lease guard is the claim lease minus the slack, so a lease equal to the slack leaves no
        // guard at all and the queue-options layer rejects it.
        using var rejected = BuildProvider(
            queue => queue.ClaimLeaseDuration = MessagingTimingPolicy.Default.LeaseSafetySlack);

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveQueueOptions(rejected));

        Assert.Contains(nameof(MessagingQueueOptions.ClaimLeaseDuration), exception.Message, StringComparison.Ordinal);

        // One tick more is the smallest lease the queue-options layer itself accepts.
        var smallestLease = MessagingTimingPolicy.Default.LeaseSafetySlack + TimeSpan.FromTicks(1);
        using var accepted = BuildProvider(queue => queue.ClaimLeaseDuration = smallestLease);

        Assert.Equal(smallestLease, ResolveQueueOptions(accepted).ClaimLeaseDuration);

        // That lease is still far too short for a real transport attempt, which is a different layer's
        // invariant: the combined WhatsApp budget must still reject it rather than declaring the host
        // configuration valid.
        using var transport = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = smallestLease,
            _ => { });

        var transportException = Assert.Throws<OptionsValidationException>(
            () => _ = ResolveWhatsAppOptions(transport));

        Assert.Contains(nameof(WhatsAppOptions.TimeoutSeconds), transportException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_lease_bound_includes_the_schedulable_ceiling_exactly()
    {
        // The guard is a scheduled cancellation, so the ceiling itself is a valid lease and one
        // millisecond beyond it is not.
        using var accepted = BuildProvider(
            queue => queue.ClaimLeaseDuration = MessagingTimingPolicy.MaxClaimLease);

        Assert.Equal(MessagingTimingPolicy.MaxClaimLease, ResolveQueueOptions(accepted).ClaimLeaseDuration);

        using var rejected = BuildProvider(
            queue => queue.ClaimLeaseDuration = MessagingTimingPolicy.MaxClaimLease + TimeSpan.FromMilliseconds(1));

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveQueueOptions(rejected));

        Assert.Contains(nameof(MessagingQueueOptions.ClaimLeaseDuration), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_transport_budget_error_never_echoes_a_WhatsApp_secret()
    {
        using var provider = BuildTransportProvider(
            queue => queue.ClaimLeaseDuration = TimeSpan.FromMinutes(1),
            whatsApp => whatsApp.TimeoutSeconds = 60);

        var exception = Assert.Throws<OptionsValidationException>(() => _ = ResolveWhatsAppOptions(provider));

        foreach (var secret in new[] { TestVerifyToken, TestAppSecret, TestAccessToken })
        {
            Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        }
    }

    private const string TestVerifyToken = "test-verify-token";
    private const string TestAppSecret = "test-app-secret";
    private const string TestAccessToken = "test-access-token";

    private static ServiceProvider BuildProvider(Action<MessagingQueueOptions> configure) =>
        new ServiceCollection()
            .AddLogging()
            .AddMessagingModule(UnusedConnectionString, configure)
            .BuildServiceProvider();

    private static ServiceProvider BuildTransportProvider(
        Action<MessagingQueueOptions> configureQueue,
        Action<WhatsAppOptions> configureWhatsApp) =>
        new ServiceCollection()
            .AddLogging()
            .AddMessagingModule(
                UnusedConnectionString,
                configureQueue,
                options =>
                {
                    options.ApiVersion = "v23.0";
                    options.PhoneNumberId = "123";
                    options.VerifyToken = TestVerifyToken;
                    options.AppSecret = TestAppSecret;
                    options.AccessToken = TestAccessToken;
                    configureWhatsApp(options);
                })
            .BuildServiceProvider();

    private static MessagingQueueOptions ResolveQueueOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<MessagingQueueOptions>>().Value;

    private static WhatsAppOptions ResolveWhatsAppOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
}
