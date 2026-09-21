using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The WhatsApp service window is refreshed from the inbound provider timestamp and lasts exactly
/// 24 hours, as the approved Issue #11 decision states.
/// </summary>
public sealed class ConversationWindowPolicyTests
{
    private static readonly DateTime Inbound = new(2026, 9, 21, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void The_service_window_is_exactly_twenty_four_hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), ConversationWindowPolicy.ServiceWindowDuration);
    }

    [Fact]
    public void An_accepted_inbound_refreshes_the_window_from_the_provider_timestamp()
    {
        Assert.Equal(
            new DateTime(2026, 9, 22, 9, 30, 0, DateTimeKind.Utc),
            ConversationWindowPolicy.Refresh(Inbound));
    }

    [Fact]
    public void An_unspecified_or_local_provider_timestamp_is_normalized_to_utc()
    {
        var unspecified = new DateTime(2026, 9, 21, 9, 30, 0, DateTimeKind.Unspecified);
        var local = new DateTime(2026, 9, 21, 12, 30, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 9, 22, 9, 30, 0, DateTimeKind.Utc),
            ConversationWindowPolicy.Refresh(unspecified));
        Assert.Equal(
            new DateTime(2026, 9, 22, 12, 30, 0, DateTimeKind.Utc),
            ConversationWindowPolicy.Refresh(local.ToLocalTime()));
    }

    [Fact]
    public void The_window_is_open_only_while_it_is_still_in_the_future()
    {
        var expiresAt = ConversationWindowPolicy.Refresh(Inbound);

        Assert.True(ConversationWindowPolicy.IsOpen(expiresAt, Inbound));
        Assert.True(ConversationWindowPolicy.IsOpen(expiresAt, expiresAt.AddSeconds(-1)));
        Assert.False(ConversationWindowPolicy.IsOpen(expiresAt, expiresAt));
        Assert.False(ConversationWindowPolicy.IsOpen(expiresAt, expiresAt.AddSeconds(1)));
    }

    [Fact]
    public void A_conversation_without_a_window_is_closed()
    {
        Assert.False(ConversationWindowPolicy.IsOpen(null, Inbound));
    }
}
