namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// The UX state is short-lived: one document, one sliding expiry, exactly 30 minutes from its last
/// successful write, as the approved Issue #11 decision states. Expired state is read as empty and is
/// replaced by the next successful write.
/// </summary>
public static class ConversationStatePolicy
{
    /// <summary>The sliding time to live of a stored state document.</summary>
    public const int TtlMinutes = 30;

    /// <summary>The sliding time to live of a stored state document.</summary>
    public static TimeSpan StateTtl { get; } = TimeSpan.FromMinutes(TtlMinutes);

    /// <summary>The expiry a successful state write records at <paramref name="utcNow"/>.</summary>
    public static DateTime Refresh(DateTime utcNow) =>
        ConversationTimestamps.ToUtc(utcNow).Add(StateTtl);

    /// <summary>
    /// True once the document is no longer live. The expiry instant itself is expired, so a document
    /// stops being usable exactly when it says it does.
    /// </summary>
    public static bool IsExpired(DateTime expiresAt, DateTime utcNow) =>
        ConversationTimestamps.ToUtc(expiresAt) <= ConversationTimestamps.ToUtc(utcNow);
}
