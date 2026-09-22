using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// A reply intent carries ordered model/variant id pairs. The contract snapshots them, so a caller that
/// keeps the collection it passed in cannot change a reply that is already on its way to the renderer,
/// and the two lists can never drift out of step.
/// </summary>
public sealed class ConversationResponseIntentTests
{
    [Fact]
    public void A_caller_that_mutates_its_own_lists_cannot_change_the_intent()
    {
        var models = new List<long> { 10, 11 };
        var variants = new List<long> { 21, 25 };

        var intent = Reply().WithCandidates(models.Zip(variants));

        models.Clear();
        variants[1] = 99;

        Assert.Equal([10, 11], intent.ModelIds);
        Assert.Equal([21, 25], intent.VariantIds);
    }

    [Fact]
    public void The_two_id_lists_are_always_paired()
    {
        var intent = Reply().WithCandidates([(10, 21), (11, 25)]);

        Assert.Equal(intent.ModelIds.Count, intent.VariantIds.Count);
        Assert.Equal(10, intent.ModelIds[0]);
        Assert.Equal(21, intent.VariantIds[0]);
        Assert.Equal(11, intent.ModelIds[1]);
        Assert.Equal(25, intent.VariantIds[1]);
    }

    [Fact]
    public void An_intent_without_candidates_exposes_empty_lists()
    {
        var intent = Reply();

        Assert.Empty(intent.ModelIds);
        Assert.Empty(intent.VariantIds);
    }

    [Fact]
    public void The_exposed_lists_cannot_be_mutated_through_the_contract()
    {
        var intent = Reply().WithCandidates([(10, 21)]);
        var exposed = Assert.IsAssignableFrom<IList<long>>(intent.ModelIds);

        Assert.Null(intent.ModelIds as List<long>);
        Assert.True(exposed.IsReadOnly);
        Assert.IsType<NotSupportedException>(Record.Exception(() => exposed.Add(11)));
    }

    [Fact]
    public void A_caller_that_mutates_the_query_it_passed_cannot_change_the_effective_search()
    {
        var ports = new List<string> { "HDMI" };
        var grades = new List<string> { "A" };
        var query = new ProductSearchQuery
        {
            Brand = "Dell",
            SizeInches = 24,
            RequiredPorts = ports,
            Grades = grades,
            Budget = ProductBudget.Hard(2500),
        };

        var intent = Reply().WithSearchQuery(query);

        ports.Add("VGA");
        grades.Clear();

        var effective = intent.SearchQuery;

        Assert.NotNull(effective);
        Assert.Equal(["HDMI"], effective.RequiredPorts);
        Assert.Equal(["A"], effective.Grades);
        Assert.Equal("Dell", effective.Brand);
        Assert.Equal(24, effective.SizeInches);
        Assert.Equal(BudgetType.Hard, effective.Budget!.Type);
        Assert.Equal(2500, effective.Budget.Target);

        // The snapshot is a copy, so the caller's own query is not the one the renderer would run.
        Assert.NotSame(query, effective);
    }

    [Fact]
    public void A_range_budget_survives_the_effective_search_snapshot()
    {
        var intent = Reply().WithSearchQuery(
            new ProductSearchQuery { Budget = ProductBudget.Range(1000m, 2000m) });

        var effective = intent.SearchQuery;

        Assert.NotNull(effective);
        Assert.Equal(BudgetType.Range, effective.Budget!.Type);
        Assert.Equal(1000, effective.Budget.Min);
        Assert.Equal(2000, effective.Budget.Max);
    }

    private static ConversationResponseIntent Reply() =>
        new()
        {
            Kind = ConversationResponseKind.ProductSearchResults,
            ConversationId = 7,
            CustomerExternalId = "20100000001",
        };
}
