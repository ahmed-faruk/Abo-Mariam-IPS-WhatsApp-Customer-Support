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
}
