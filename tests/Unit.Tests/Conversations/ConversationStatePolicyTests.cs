using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The UX state slides forward on every successful write and expires as one document, as the approved
/// Issue #11 decision states.
/// </summary>
public sealed class ConversationStatePolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_ttl_is_exactly_thirty_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ConversationStatePolicy.StateTtl);
        Assert.Equal(30, ConversationStatePolicy.TtlMinutes);
    }

    [Fact]
    public void Every_write_sets_expiry_thirty_minutes_after_the_write_instant()
    {
        Assert.Equal(
            new DateTime(2026, 9, 21, 12, 30, 0, DateTimeKind.Utc),
            ConversationStatePolicy.Refresh(Now));
    }

    [Fact]
    public void A_later_write_slides_the_expiry_forward()
    {
        var first = ConversationStatePolicy.Refresh(Now);
        var second = ConversationStatePolicy.Refresh(Now.AddMinutes(20));

        // The expiry moves by exactly the time that elapsed between the two writes.
        Assert.Equal(TimeSpan.FromMinutes(20), second - first);
        Assert.True(second > first);
    }

    [Fact]
    public void State_is_live_until_the_expiry_instant_and_expired_from_it()
    {
        var expiry = ConversationStatePolicy.Refresh(Now);

        Assert.False(ConversationStatePolicy.IsExpired(expiry, Now));
        Assert.False(ConversationStatePolicy.IsExpired(expiry, Now.AddMinutes(29)));
        Assert.True(ConversationStatePolicy.IsExpired(expiry, expiry));
        Assert.True(ConversationStatePolicy.IsExpired(expiry, Now.AddMinutes(31)));
    }
}
