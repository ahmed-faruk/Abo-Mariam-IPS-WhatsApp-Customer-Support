namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// One rule for the timestamps Conversations stores: every instant is UTC. A provider timestamp with
/// no kind is read as UTC rather than as the host's local time, so the service window never depends on
/// where the demo machine happens to run.
/// </summary>
public static class ConversationTimestamps
{
    /// <summary>The instant in UTC, interpreting an unspecified kind as UTC.</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
