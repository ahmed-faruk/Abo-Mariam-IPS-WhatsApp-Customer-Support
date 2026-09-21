using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The bounded context carries only reference material for a later turn. It never carries commercial
/// facts, and an oversized or blank context is rejected before it can reach a request.
/// </summary>
public sealed class NluConversationContextTests
{
    [Fact]
    public void The_empty_context_carries_nothing()
    {
        Assert.True(NluConversationContext.Empty.IsEmpty);
        NluConversationContext.Empty.EnsureWithinBounds();
    }

    [Fact]
    public void A_bounded_context_is_accepted()
    {
        var context = new NluConversationContext
        {
            PreviousCandidateLabels = ["الأولى", "التانية"],
            PreviousReference = "الأولى",
        };

        Assert.False(context.IsEmpty);
        context.EnsureWithinBounds();
    }

    [Fact]
    public void Too_many_previous_candidates_are_rejected()
    {
        var context = new NluConversationContext
        {
            PreviousCandidateLabels =
                [.. Enumerable.Range(0, NluConversationContext.MaxCandidateLabels + 1).Select(index => $"item {index}")],
        };

        Assert.Throws<ArgumentException>(context.EnsureWithinBounds);
    }

    [Fact]
    public void A_blank_previous_candidate_label_is_rejected()
    {
        var context = new NluConversationContext { PreviousCandidateLabels = ["   "] };

        Assert.Throws<ArgumentException>(context.EnsureWithinBounds);
    }

    [Fact]
    public void An_overlong_previous_reference_is_rejected()
    {
        var context = new NluConversationContext
        {
            PreviousReference = new string('x', NluConversationContext.MaxLabelLength + 1),
        };

        Assert.Throws<ArgumentException>(context.EnsureWithinBounds);
    }

    [Fact]
    public void A_null_label_collection_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(
            () => _ = new NluConversationContext { PreviousCandidateLabels = null! });
    }

    [Fact]
    public void A_context_snapshots_the_callers_collection()
    {
        var labels = new List<string> { "الأولى", "التانية" };
        var context = new NluConversationContext { PreviousCandidateLabels = labels };

        labels[0] = "mutated";
        labels.Add("التالتة");
        labels.Clear();

        Assert.Equal(["الأولى", "التانية"], context.PreviousCandidateLabels);
        Assert.False(context.IsEmpty);
        context.EnsureWithinBounds();
    }

    [Fact]
    public void A_later_caller_mutation_cannot_push_the_context_over_its_bound()
    {
        var labels = new List<string>(
            Enumerable.Range(0, NluConversationContext.MaxCandidateLabels).Select(index => $"item {index}"));
        var context = new NluConversationContext { PreviousCandidateLabels = labels };

        labels.Add("one more");

        Assert.Equal(NluConversationContext.MaxCandidateLabels, context.PreviousCandidateLabels.Count);
        context.EnsureWithinBounds();
    }

    [Fact]
    public void The_exposed_labels_cannot_be_mutated_through_the_contract()
    {
        var context = new NluConversationContext { PreviousCandidateLabels = ["الأولى"] };

        var labels = Assert.IsAssignableFrom<IList<string>>(context.PreviousCandidateLabels);

        Assert.Throws<NotSupportedException>(() => labels.Add("التانية"));
        Assert.Throws<NotSupportedException>(() => labels[0] = "mutated");
        Assert.Equal(["الأولى"], context.PreviousCandidateLabels);
    }
}
