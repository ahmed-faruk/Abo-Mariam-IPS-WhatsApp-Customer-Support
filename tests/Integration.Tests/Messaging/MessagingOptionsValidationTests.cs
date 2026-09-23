using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private static ServiceProvider BuildProvider(Action<MessagingQueueOptions> configure) =>
        new ServiceCollection()
            .AddLogging()
            .AddMessagingModule(UnusedConnectionString, configure)
            .BuildServiceProvider();

    private static MessagingQueueOptions ResolveQueueOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<MessagingQueueOptions>>().Value;
}
