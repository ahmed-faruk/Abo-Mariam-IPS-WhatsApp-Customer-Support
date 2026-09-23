using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// Rejects a configuration whose enforced budgets cannot fit inside the Outbox claim lease. The
/// Outbox worker claims a message immediately before it sends it, so the claim that authorizes one
/// provider attempt must stay valid for the whole attempt: a lease that expires mid-attempt lets
/// another replica recover the same message and send it a second time while the first request may
/// still be accepted by Meta, which is exactly the duplicate docs/TECHNICAL.md section 15 rules out
/// for horizontally safe workers.
/// </summary>
/// <remarks>
/// This validator is the configuration sanity gate, not the safety mechanism. The hard mechanism is
/// the worker's own lease guard, which is started before the claim and ends the attempt, the
/// completion bookkeeping and the failure bookkeeping before the database lease can be recovered.
/// The validator only ensures that a normal configured operation - the bounded overall claim path, the
/// whole provider attempt, the shared completion-bookkeeping budget and the lease-safety slack - fits
/// inside the lease, and the constants it uses are the ones the workers actually enforce. The
/// per-statement command timeout is a separate bound and is deliberately not the claim term.
/// </remarks>
internal sealed class WhatsAppAttemptBudgetValidator(
    IOptions<MessagingQueueOptions> queue,
    MessagingTimingPolicy timing) : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timing);

        var required = timing.RequiredClaimLease(options.TimeoutSeconds);
        var lease = queue.Value.ClaimLeaseDuration;

        if (lease > required)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"The WhatsApp setting '{nameof(WhatsAppOptions.TimeoutSeconds)}' does not fit inside the "
            + $"messaging queue setting '{nameof(MessagingQueueOptions.ClaimLeaseDuration)}': the worker "
            + $"needs a claim lease longer than {required} - the bounded overall claim path "
            + $"({timing.ClaimBudget}), the whole Meta attempt, the shared completion "
            + $"bookkeeping budget ({timing.CompletionBookkeepingBudget}) and the lease-safety slack "
            + $"({timing.LeaseSafetySlack}) - but the configured lease is {lease}.");
    }
}
