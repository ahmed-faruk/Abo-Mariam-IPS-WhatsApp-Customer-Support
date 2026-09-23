using Microsoft.Extensions.Options;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Rejects a queue policy that would stall the workers instead of polling. Every count and every
/// delay must be positive: a nonpositive batch size or attempt limit makes every poll throw, which
/// leaves the durable queue untouched while the worker logs the same failure forever.
/// </summary>
internal sealed class MessagingQueueOptionsValidator : IValidateOptions<MessagingQueueOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        RequirePositive(failures, nameof(MessagingQueueOptions.InboxBatchSize), options.InboxBatchSize);
        RequirePositive(failures, nameof(MessagingQueueOptions.OutboxBatchSize), options.OutboxBatchSize);
        RequirePositive(failures, nameof(MessagingQueueOptions.InboxMaxAttempts), options.InboxMaxAttempts);
        RequirePositive(failures, nameof(MessagingQueueOptions.InboxRetryDelay), options.InboxRetryDelay);
        RequirePositive(failures, nameof(MessagingQueueOptions.OutboxRetryDelay), options.OutboxRetryDelay);
        RequirePositive(failures, nameof(MessagingQueueOptions.OutboxMaxRetryDelay), options.OutboxMaxRetryDelay);
        RequirePositive(failures, nameof(MessagingQueueOptions.ClaimLeaseDuration), options.ClaimLeaseDuration);
        RequirePositive(failures, nameof(MessagingQueueOptions.IdlePollDelay), options.IdlePollDelay);
        RequireAtMost(
            failures,
            nameof(MessagingQueueOptions.OutboxRetryDelay),
            options.OutboxRetryDelay,
            nameof(MessagingQueueOptions.OutboxMaxRetryDelay),
            options.OutboxMaxRetryDelay);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string setting, int value)
    {
        if (value < 1)
        {
            failures.Add($"The messaging queue setting '{setting}' must be greater than zero but was {value}.");
        }
    }

    private static void RequirePositive(List<string> failures, string setting, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"The messaging queue setting '{setting}' must be a positive duration but was {value}.");
        }
    }

    private static void RequireAtMost(
        List<string> failures,
        string setting,
        TimeSpan value,
        string ceilingSetting,
        TimeSpan ceiling)
    {
        if (value > ceiling)
        {
            failures.Add(
                $"The messaging queue setting '{setting}' must not exceed '{ceilingSetting}' "
                + $"but was {value} against {ceiling}.");
        }
    }
}
