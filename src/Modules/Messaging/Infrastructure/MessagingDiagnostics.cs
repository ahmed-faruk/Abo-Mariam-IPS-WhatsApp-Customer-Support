using System.Globalization;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;

/// <summary>
/// The stable, bounded diagnostics Messaging persists for an attempt whose outcome is not a plain
/// provider rejection. Every one is a fixed classification, optionally plus the exception category:
/// no provider free text, no exception message and no customer value can reach the durable queue.
/// </summary>
internal static class MessagingDiagnostics
{
    private const string UnknownOutcomePrefix = "Unknown provider outcome: ";

    /// <summary>The sender reported an unknown outcome without a diagnostic of its own.</summary>
    public const string AcceptanceNotEstablished =
        UnknownOutcomePrefix + "acceptance could not be established.";

    /// <summary>
    /// A claimed attempt was abandoned. Its provider outcome is not knowable from the queue, so the
    /// recovery is recorded as uncertain instead of as a refusal.
    /// </summary>
    public const string ExpiredClaimRecovered =
        UnknownOutcomePrefix + "expired claim recovered; the previous attempt outcome is unknown.";

    /// <summary>An abandoned attempt that has spent its whole attempt budget.</summary>
    public const string ExpiredClaimExhausted =
        UnknownOutcomePrefix + "expired claim reached max attempts.";

    /// <summary>An outbound attempt threw instead of reporting an outcome.</summary>
    public static string UnexpectedOutboundFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return string.Create(CultureInfo.InvariantCulture, $"{UnknownOutcomePrefix}{exception.GetType().Name}");
    }

    /// <summary>An inbound message failed in orchestration rather than in the queue.</summary>
    public static string UnexpectedInboundFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return string.Create(CultureInfo.InvariantCulture, $"Processing failed: {exception.GetType().Name}");
    }
}
