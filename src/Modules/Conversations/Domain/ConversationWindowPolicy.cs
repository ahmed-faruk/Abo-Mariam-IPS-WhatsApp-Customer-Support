namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// The WhatsApp service window of docs/TECHNICAL.md section 17. The window is refreshed only by an
/// accepted inbound customer message and lasts exactly 24 hours from that message's provider
/// timestamp, as the approved Issue #11 decision states.
/// </summary>
public static class ConversationWindowPolicy
{
    /// <summary>How long a reactive free-form reply stays allowed after the last inbound message.</summary>
    public static TimeSpan ServiceWindowDuration { get; } = TimeSpan.FromHours(24);

    /// <summary>The expiry of a conversation whose last accepted inbound was <paramref name="providerTimestamp"/>.</summary>
    public static DateTime Refresh(DateTime providerTimestamp) =>
        ConversationTimestamps.ToUtc(providerTimestamp).Add(ServiceWindowDuration);

    /// <summary>
    /// True while the window is still in the future. The expiry instant itself is closed, so a reply
    /// is never enqueued exactly at the boundary.
    /// </summary>
    public static bool IsOpen(DateTime? windowExpiresAt, DateTime utcNow) =>
        windowExpiresAt is { } expiresAt
        && ConversationTimestamps.ToUtc(expiresAt) > ConversationTimestamps.ToUtc(utcNow);
}
