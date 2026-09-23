using System.Threading.RateLimiting;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;

/// <summary>
/// The bounded delivery budget of the public Meta webhook. A permit is taken once per notification
/// whose <c>X-Hub-Signature-256</c> has already been verified, so traffic that cannot prove it came
/// from Meta can never consume the permits a genuine callback needs. It lives with the webhook
/// transport that uses it, and it is one fixed window in the process, which is the built-in
/// mechanism of docs/TECHNICAL.md section 19 that needs no external store.
/// </summary>
internal sealed class WhatsAppWebhookDeliveryLimiter : IDisposable
{
    private readonly FixedWindowRateLimiter limiter;

    public WhatsAppWebhookDeliveryLimiter(WhatsAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.WebhookPermitLimit,
            Window = TimeSpan.FromSeconds(options.WebhookWindowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>True while this window still has a permit for another authenticated delivery.</summary>
    public bool TryAcquire()
    {
        using var lease = limiter.AttemptAcquire(1);

        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
