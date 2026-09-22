using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The advisory lock identity of a conversation is derived from the whole conversation id, so two
/// unrelated conversations are never serialized against each other merely because their ids share
/// low bits. The identity is a pure function of the id alone, so every application replica derives
/// the same key for the same conversation.
/// </summary>
public sealed class ConversationOperationCoordinatorTests
{
    private const long TwoToThe32 = 4_294_967_296L;

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(7_000_000_000L)]
    public void Two_conversations_that_share_their_low_32_bits_do_not_share_a_lock_key(long conversationId)
    {
        Assert.NotEqual(
            ConversationOperationCoordinator.LockKeyFor(conversationId),
            ConversationOperationCoordinator.LockKeyFor(conversationId + TwoToThe32));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(1L << 40)]
    [InlineData(long.MaxValue)]
    public void The_same_conversation_always_derives_the_same_lock_key(long conversationId)
    {
        Assert.Equal(
            ConversationOperationCoordinator.LockKeyFor(conversationId),
            ConversationOperationCoordinator.LockKeyFor(conversationId));
    }

    [Theory]
    [InlineData(1L, 2L)]
    [InlineData(1L, 1L << 32)]
    [InlineData(1L << 32, 1L << 40)]
    [InlineData(1L << 62, long.MaxValue)]
    [InlineData(long.MaxValue - 1, long.MaxValue)]
    public void Different_conversation_ids_derive_different_lock_keys(long first, long second)
    {
        Assert.NotEqual(
            ConversationOperationCoordinator.LockKeyFor(first),
            ConversationOperationCoordinator.LockKeyFor(second));
    }

    [Fact]
    public void The_largest_conversation_id_derives_a_usable_lock_key()
    {
        // No id of the positive Int64 domain may lose its own identity to truncation, so the largest
        // one has to stay distinct from the id whose bits are its own predecessor.
        Assert.NotEqual(
            ConversationOperationCoordinator.LockKeyFor(long.MaxValue),
            ConversationOperationCoordinator.LockKeyFor(long.MaxValue - 1));

        Assert.NotEqual(
            ConversationOperationCoordinator.LockKeyFor(long.MaxValue),
            ConversationOperationCoordinator.LockKeyFor(long.MaxValue - TwoToThe32));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void A_conversation_id_outside_the_positive_domain_has_no_lock_key(long conversationId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConversationOperationCoordinator.LockKeyFor(conversationId));
    }
}
