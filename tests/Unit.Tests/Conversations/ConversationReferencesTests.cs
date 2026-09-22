using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// Follow-up references resolve through an explicit deterministic allowlist. Nothing is guessed: an
/// unknown phrase, an empty shortlist or a missing stored reference all end in a clarification, and
/// the first position is never used as a silent fallback for "this one".
/// </summary>
public sealed class ConversationReferencesTests
{
    private static readonly ConversationStateDocument TwoItemState = ConversationStateDocument.Empty with
    {
        Shortlist = ConversationStateDocument.BuildShortlist([(10, 21), (11, 25)]),
        LastModelId = 11,
        LastVariantId = 25,
    };

    [Theory]
    [InlineData("first")]
    [InlineData("First")]
    [InlineData(" the first ")]
    [InlineData("1st")]
    [InlineData("الأولى")]
    [InlineData("الاولى")]
    [InlineData("الأول")]
    [InlineData("الاول")]
    public void The_first_position_aliases_resolve_the_first_shortlist_item(string reference)
    {
        var resolution = ConversationReferences.Resolve(reference, TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(10, resolution.ModelId);
        Assert.Equal(21, resolution.VariantId);
    }

    [Theory]
    [InlineData("second")]
    [InlineData("Second")]
    [InlineData(" the second ")]
    [InlineData("2nd")]
    [InlineData("التانية")]
    [InlineData("الثانية")]
    [InlineData("التاني")]
    [InlineData("الثاني")]
    public void The_second_position_aliases_resolve_the_second_shortlist_item(string reference)
    {
        var resolution = ConversationReferences.Resolve(reference, TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("this")]
    [InlineData("this one")]
    [InlineData("the one")]
    [InlineData("دي")]
    [InlineData("ده")]
    [InlineData("الديل اللي قولتلي عليها")]
    public void The_current_aliases_resolve_the_stored_reference_and_never_the_first_position(string reference)
    {
        var resolution = ConversationReferences.Resolve(reference, TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("third")]
    [InlineData("التالت")]
    [InlineData("the cheapest one")]
    [InlineData("الأولى دي")]
    public void An_unknown_or_absent_reference_is_not_guessed(string? reference)
    {
        var resolution = ConversationReferences.Resolve(reference, TwoItemState);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.UnresolvedReference, resolution.ReasonCode);
    }

    [Fact]
    public void The_first_position_of_an_empty_shortlist_is_a_clarification()
    {
        var resolution = ConversationReferences.Resolve("first", ConversationStateDocument.Empty);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.ShortlistEmpty, resolution.ReasonCode);
    }

    [Fact]
    public void The_second_position_of_a_one_item_shortlist_is_a_clarification()
    {
        var state = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21)]),
        };

        var resolution = ConversationReferences.Resolve("second", state);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.ShortlistPositionMissing, resolution.ReasonCode);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("this")]
    [InlineData("the one")]
    [InlineData("ده")]
    public void The_current_aliases_without_a_stored_reference_are_a_clarification(string reference)
    {
        var state = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21)]),
        };

        var resolution = ConversationReferences.Resolve(reference, state);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.CurrentReferenceMissing, resolution.ReasonCode);
    }

    [Fact]
    public void The_current_aliases_keep_working_after_the_shortlist_expired_away()
    {
        var state = ConversationStateDocument.Empty with
        {
            LastModelId = 11,
            LastVariantId = 25,
        };

        var resolution = ConversationReferences.Resolve("this one", state);

        Assert.True(resolution.IsResolved);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Fact]
    public void Every_position_alias_is_distinct_and_matches_exactly_one_target()
    {
        var targets = ConversationReferences.Aliases
            .GroupBy(alias => alias.NormalizedText, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(targets);
        Assert.All(ConversationReferences.Aliases, alias => Assert.False(string.IsNullOrWhiteSpace(alias.NormalizedText)));
        Assert.Contains(ConversationReferences.Aliases, alias => alias.Target == ConversationReferenceTarget.Current);
        Assert.Contains(ConversationReferences.Aliases, alias => alias.Target == ConversationReferenceTarget.First);
        Assert.Contains(ConversationReferences.Aliases, alias => alias.Target == ConversationReferenceTarget.Second);
    }
}
