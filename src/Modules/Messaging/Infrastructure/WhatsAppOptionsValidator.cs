using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

internal sealed class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        RequireText(failures, nameof(WhatsAppOptions.ApiVersion), options.ApiVersion);
        RequireText(failures, nameof(WhatsAppOptions.PhoneNumberId), options.PhoneNumberId);
        RequireText(failures, nameof(WhatsAppOptions.VerifyToken), options.VerifyToken);
        RequireText(failures, nameof(WhatsAppOptions.AppSecret), options.AppSecret);
        RequireText(failures, nameof(WhatsAppOptions.AccessToken), options.AccessToken);
        RequirePositive(failures, nameof(WhatsAppOptions.TimeoutSeconds), options.TimeoutSeconds);
        RequireAtLeast(
            failures,
            nameof(WhatsAppOptions.MaxWebhookBodyBytes),
            options.MaxWebhookBodyBytes,
            WhatsAppOptions.DefaultMaxWebhookBodyBytes);
        RequirePositive(failures, nameof(WhatsAppOptions.WebhookPermitLimit), options.WebhookPermitLimit);
        RequirePositive(failures, nameof(WhatsAppOptions.WebhookWindowSeconds), options.WebhookWindowSeconds);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireText(List<string> failures, string setting, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"The WhatsApp setting '{setting}' is required.");
        }
    }

    private static void RequirePositive(List<string> failures, string setting, int value)
    {
        if (value < 1)
        {
            failures.Add($"The WhatsApp setting '{setting}' must be greater than zero but was {value}.");
        }
    }

    private static void RequireAtLeast(List<string> failures, string setting, int value, int minimum)
    {
        if (value < minimum)
        {
            failures.Add($"The WhatsApp setting '{setting}' must be at least {minimum} bytes but was {value}.");
        }
    }
}
