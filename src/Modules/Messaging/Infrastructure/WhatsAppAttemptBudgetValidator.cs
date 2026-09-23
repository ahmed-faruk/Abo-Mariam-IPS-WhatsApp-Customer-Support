using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Rejects a transport attempt budget that cannot finish inside the Outbox claim lease. The Outbox
/// worker claims a message immediately before it sends it, so the claim that authorizes one provider
/// attempt must stay valid for the whole attempt: a lease that expires mid-attempt lets another
/// replica recover the same message and send it a second time while the first request may still be
/// accepted by Meta, which is exactly the duplicate docs/TECHNICAL.md section 14 rules out.
/// </summary>
internal sealed class WhatsAppAttemptBudgetValidator(IOptions<MessagingQueueOptions> queue)
    : IValidateOptions<WhatsAppOptions>
{
    /// <summary>
    /// The room kept between the end of one provider attempt and the expiry of the claim that
    /// authorized it. It covers the bounded completion bookkeeping the Outbox worker performs after a
    /// successful send - a small, fixed number of store writes with no artificial delay of their own -
    /// so the duration of those writes is bounded by the database rather than by configuration, and
    /// this policy names the separation instead of leaving it to whatever the database happens to do.
    /// </summary>
    internal static readonly TimeSpan CompletionBookkeepingSafetyMargin = TimeSpan.FromSeconds(30);

    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var attempt = TimeSpan.FromSeconds(options.TimeoutSeconds) + CompletionBookkeepingSafetyMargin;
        var lease = queue.Value.ClaimLeaseDuration;

        if (lease > attempt)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"The WhatsApp setting '{nameof(WhatsAppOptions.TimeoutSeconds)}' must leave room for its "
            + $"completion bookkeeping inside the messaging queue setting "
            + $"'{nameof(MessagingQueueOptions.ClaimLeaseDuration)}': the complete outbound attempt "
            + $"({attempt}) must stay strictly shorter than the claim lease ({lease}).");
    }
}
