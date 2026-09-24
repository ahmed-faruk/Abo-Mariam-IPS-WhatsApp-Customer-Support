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

    [Fact]
    public void A_paraphrased_reference_is_recovered_from_the_customer_text()
    {
        // The captured DEMO-07 reply of Issue #32 paraphrases the reference; the customer text names the
        // existing Current phrase, so the recovery resolves the stored current product.
        var resolution = ConversationReferences.ResolveWithCustomerText(
            "the previous monitor",
            "الديل اللي قولتلي عليها لسه موجودة؟",
            TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(ConversationReferenceTarget.Current, resolution.Target);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Theory]
    [InlineData("that thing")]
    [InlineData("the dell one")]
    public void The_original_customer_text_drives_the_recovery_not_the_model_phrase(string modelReference)
    {
        var resolution = ConversationReferences.ResolveWithCustomerText(
            modelReference,
            "ده لسه موجود؟",
            TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(ConversationReferenceTarget.Current, resolution.Target);
        Assert.Equal(11, resolution.ModelId);
    }

    [Fact]
    public void A_recovered_reference_resolves_across_a_punctuation_boundary()
    {
        var resolution = ConversationReferences.ResolveWithCustomerText("that thing", "دي؟", TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Theory]
    [InlineData("الأولى دي")]
    [InlineData("عندك ديل 24؟")]
    [InlineData("عندي بديل")]
    [InlineData("unknown text")]
    [InlineData("")]
    [InlineData(null)]
    public void An_ambiguous_absent_or_single_token_reference_stays_unresolved(string? customerText)
    {
        var resolution = ConversationReferences.ResolveWithCustomerText(
            "the previous monitor",
            customerText,
            TwoItemState);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.UnresolvedReference, resolution.ReasonCode);
    }

    [Theory]
    [InlineData("الأولى والتانية؟")]
    [InlineData("الأولى والثانية؟")]
    public void Two_positions_joined_by_one_conjunction_are_an_ambiguous_reference(string customerText)
    {
        // "الأولى والتانية" names First and Second at once: recovering either one alone would answer
        // about a product the customer did not mean, so the reference stays a clarification.
        var resolution = ConversationReferences.ResolveWithCustomerText(
            "the previous monitor",
            customerText,
            TwoItemState);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.UnresolvedReference, resolution.ReasonCode);
    }

    [Theory]
    [InlineData("والتانية؟")]
    [InlineData("والديل اللي قولتلي عليها")]
    public void One_conjunction_prefixed_alias_is_still_matched(string customerText)
    {
        var resolution = ConversationReferences.ResolveWithCustomerText(
            "the previous monitor",
            customerText,
            TwoItemState);

        Assert.True(resolution.IsResolved);
    }

    [Fact]
    public void The_normal_allowlisted_resolution_wins_before_the_customer_text_is_inspected()
    {
        var resolution = ConversationReferences.ResolveWithCustomerText("first", "دي", TwoItemState);

        Assert.True(resolution.IsResolved);
        Assert.Equal(ConversationReferenceTarget.First, resolution.Target);
        Assert.Equal(10, resolution.ModelId);
        Assert.Equal(21, resolution.VariantId);
    }

    [Fact]
    public void A_missing_shortlist_target_still_lets_the_customer_text_recover_a_valid_target()
    {
        // The model named the second position, but the stored state has only one item. The original
        // customer text names one existing Current alias, so recovery resolves the stored current product
        // instead of answering with the missing position.
        var state = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21)]),
            LastModelId = 11,
            LastVariantId = 25,
        };

        var resolution = ConversationReferences.ResolveWithCustomerText("second", "دي؟", state);

        Assert.True(resolution.IsResolved);
        Assert.Equal(ConversationReferenceTarget.Current, resolution.Target);
        Assert.Equal(11, resolution.ModelId);
        Assert.Equal(25, resolution.VariantId);
    }

    [Fact]
    public void A_recovered_target_that_cannot_resolve_reports_its_own_reason()
    {
        var state = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21)]),
        };

        var resolution = ConversationReferences.ResolveWithCustomerText("second", "دي؟", state);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.CurrentReferenceMissing, resolution.ReasonCode);
    }

    [Fact]
    public void A_recovered_current_reference_without_a_stored_current_stays_a_clarification()
    {
        var resolution = ConversationReferences.ResolveWithCustomerText(
            "the previous monitor",
            "الديل اللي قولتلي عليها",
            ConversationStateDocument.Empty);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.CurrentReferenceMissing, resolution.ReasonCode);
    }

    [Fact]
    public void A_null_model_reference_is_never_recovered_from_the_text()
    {
        var resolution = ConversationReferences.ResolveWithCustomerText(null, "دي", TwoItemState);

        Assert.False(resolution.IsResolved);
        Assert.Equal(ConversationReferenceReasons.UnresolvedReference, resolution.ReasonCode);
    }

    [Fact]
    public void The_allowlist_is_still_exactly_the_approved_seven_per_target_set()
    {
        (string Text, ConversationReferenceTarget Target)[] expected =
        [
            ("first", ConversationReferenceTarget.First),
            ("the first", ConversationReferenceTarget.First),
            ("1st", ConversationReferenceTarget.First),
            ("الأولى", ConversationReferenceTarget.First),
            ("الاولى", ConversationReferenceTarget.First),
            ("الأول", ConversationReferenceTarget.First),
            ("الاول", ConversationReferenceTarget.First),
            ("second", ConversationReferenceTarget.Second),
            ("the second", ConversationReferenceTarget.Second),
            ("2nd", ConversationReferenceTarget.Second),
            ("التانية", ConversationReferenceTarget.Second),
            ("الثانية", ConversationReferenceTarget.Second),
            ("التاني", ConversationReferenceTarget.Second),
            ("الثاني", ConversationReferenceTarget.Second),
            ("current", ConversationReferenceTarget.Current),
            ("this", ConversationReferenceTarget.Current),
            ("this one", ConversationReferenceTarget.Current),
            ("the one", ConversationReferenceTarget.Current),
            ("دي", ConversationReferenceTarget.Current),
            ("ده", ConversationReferenceTarget.Current),
            ("الديل اللي قولتلي عليها", ConversationReferenceTarget.Current),
        ];

        Assert.Equal(
            expected,
            ConversationReferences.Aliases.Select(alias => (alias.NormalizedText, alias.Target)));
        Assert.DoesNotContain(ConversationReferences.Aliases, alias => alias.NormalizedText == "الديل");
    }
}
