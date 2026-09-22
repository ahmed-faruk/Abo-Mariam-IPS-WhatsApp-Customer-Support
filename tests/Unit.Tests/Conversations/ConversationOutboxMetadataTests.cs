using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The tiny Conversations payload stored next to an immutable Outbox reply. It carries reference
/// bookkeeping only, it is read back exactly as it was written, and anything it cannot trust - a payload
/// of another version, a malformed payload, an impossible candidate - is an invariant failure instead of
/// a reason to guess at what the customer was sent.
/// </summary>
public sealed class ConversationOutboxMetadataTests
{
    [Fact]
    public void A_payload_round_trips_its_displayed_order_and_its_handoff_effect()
    {
        var metadata = ConversationOutboxMetadata.For(
            [new ConversationDisplayedCandidate(10, 21), new ConversationDisplayedCandidate(11, 25)],
            entersHumanMode: true,
            modeRevision: 4);

        var parsed = ConversationOutboxMetadata.Parse(metadata.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(ConversationOutboxMetadata.CurrentVersion, parsed.Version);
        Assert.True(parsed.EntersHumanMode);
        Assert.Equal(4, parsed.ModeRevision);
        Assert.Equal(
            [(10L, 21L), (11L, 25L)],
            parsed.DisplayedCandidates.Select(candidate => (candidate.ModelId, candidate.VariantId)));
    }

    [Fact]
    public void A_reply_that_displays_nothing_carries_no_displayed_candidates()
    {
        var parsed = ConversationOutboxMetadata.Parse(
            ConversationOutboxMetadata.For([], entersHumanMode: false).ToJson());

        Assert.NotNull(parsed);
        Assert.Empty(parsed.DisplayedCandidates);
        Assert.False(parsed.EntersHumanMode);

        // A reply that changes no mode names no mode decision it belongs to.
        Assert.Null(parsed.ModeRevision);
    }

    [Fact]
    public void A_handoff_is_never_written_without_the_mode_revision_it_belongs_to() =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.For(
            [],
            entersHumanMode: true));

    [Fact]
    public void A_reply_that_changes_no_mode_never_records_a_mode_revision() =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.For(
            [],
            entersHumanMode: false,
            modeRevision: 3));

    [Fact]
    public void A_handoff_whose_recorded_revision_is_impossible_is_never_written() =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.For(
            [],
            entersHumanMode: true,
            modeRevision: -1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reply_stored_before_this_version_has_no_payload_at_all(string? json) =>
        Assert.Null(ConversationOutboxMetadata.Parse(json));

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"v":99}""")]
    [InlineData("""{"v":1,"displayed":[{"m":0,"v":21}]}""")]
    public void A_payload_that_cannot_be_trusted_is_an_invariant_failure(string json) =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.Parse(json));

    [Theory]
    [InlineData(10, 21, 10, 21)]
    [InlineData(10, 21, 10, 25)]
    public void A_reply_that_would_display_one_model_twice_is_never_written(
        long firstModelId,
        long firstVariantId,
        long secondModelId,
        long secondVariantId) =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.For(
            [
                new ConversationDisplayedCandidate(firstModelId, firstVariantId),
                new ConversationDisplayedCandidate(secondModelId, secondVariantId),
            ],
            entersHumanMode: false));

    [Theory]
    [InlineData("""{"v":1,"displayed":[{"m":10,"v":21},{"m":10,"v":21}]}""")]
    [InlineData("""{"v":1,"displayed":[{"m":10,"v":21},{"m":10,"v":25}]}""")]
    public void A_stored_payload_that_names_one_model_twice_is_an_invariant_failure(string json) =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.Parse(json));

    [Theory]
    [InlineData("""{"v":1,"human":true}""")]
    [InlineData("""{"v":1,"human":true,"modeRevision":-2}""")]
    [InlineData("""{"v":1,"human":false,"modeRevision":2}""")]
    public void A_stored_payload_whose_mode_effect_cannot_be_checked_is_an_invariant_failure(string json) =>
        Assert.Throws<InvalidOperationException>(() => ConversationOutboxMetadata.Parse(json));
}
