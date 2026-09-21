using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The UX state document is short-lived reference material: a shortlist of identifiers, the current
/// reference, the last intent and the customer's filters. It is never the authority for a commercial
/// fact, so nothing in the stored shape may hold a price, a quantity or a business answer.
/// </summary>
public sealed class ConversationStateDocumentTests
{
    [Fact]
    public void An_empty_document_serializes_and_round_trips()
    {
        var json = ConversationStateDocument.Empty.ToJson();

        var reloaded = ConversationStateDocument.Deserialize(json);

        Assert.Equal(ConversationStateDocument.Empty, reloaded);
        Assert.Empty(reloaded.Shortlist);
        Assert.Null(reloaded.LastModelId);
        Assert.Null(reloaded.LastVariantId);
        Assert.Null(reloaded.LastIntent);
        Assert.Null(reloaded.LastFilters);
    }

    [Fact]
    public void A_full_document_round_trips_every_storeable_field()
    {
        var document = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21), (11, 25)]),
            LastModelId = 10,
            LastVariantId = 21,
            LastIntent = "ProductSearch",
            LastFilters = new ConversationStateFilters
            {
                Brand = "Dell",
                ModelCode = "P2419H",
                SizeInches = 24,
                Panel = "IPS",
                Resolution = "1920x1080",
                MinRefreshRate = 75,
                RequiredPorts = ["HDMI", "DisplayPort"],
                Grades = ["A", "B"],
                BudgetType = BudgetType.Soft,
                BudgetTarget = 3000,
                BudgetMin = null,
                BudgetMax = null,
                UseCase = "Programming",
            },
        };

        var reloaded = ConversationStateDocument.Deserialize(document.ToJson());

        Assert.Equal(document, reloaded);
        Assert.Equal([1, 2], reloaded.Shortlist.Select(entry => entry.Position));
        Assert.Equal([10, 11], reloaded.Shortlist.Select(entry => entry.ModelId));
        Assert.Equal([21, 25], reloaded.Shortlist.Select(entry => entry.VariantId));
        Assert.Equal(BudgetType.Soft, reloaded.LastFilters!.BudgetType);
        Assert.Equal(["HDMI", "DisplayPort"], reloaded.LastFilters.RequiredPorts);
    }

    [Fact]
    public void Shortlist_positions_are_contiguous_one_based_display_positions()
    {
        var shortlist = ConversationStateDocument.BuildShortlist([(10, 21), (11, 25), (12, 30)]);

        Assert.Equal([1, 2, 3], shortlist.Select(entry => entry.Position));
    }

    [Fact]
    public void A_shortlist_holding_duplicate_models_keeps_the_first_position_of_each_model()
    {
        var shortlist = ConversationStateDocument.BuildShortlist([(10, 21), (11, 25), (10, 99)]);

        Assert.Equal([1, 2], shortlist.Select(entry => entry.Position));
        Assert.Equal([10, 11], shortlist.Select(entry => entry.ModelId));
        Assert.Equal([21, 25], shortlist.Select(entry => entry.VariantId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"shortlist":"not an array"}""")]
    public void Malformed_state_is_treated_as_empty_instead_of_throwing(string? json)
    {
        Assert.Equal(ConversationStateDocument.Empty, ConversationStateDocument.Deserialize(json));
    }

    [Theory]
    // A null collection is not a shape the state contract can express, so the document is read as empty
    // instead of throwing while it is normalized.
    [InlineData("""{"shortlist":null}""")]
    [InlineData("""{"shortlist":[null]}""")]
    [InlineData("""{"lastFilters":{"requiredPorts":null}}""")]
    [InlineData("""{"lastFilters":{"grades":null}}""")]
    [InlineData("""{"lastFilters":{"requiredPorts":["HDMI",null]}}""")]
    // Positions are the numbering the customer already heard, so a shortlist whose numbering is not a
    // contiguous one-based display order cannot be trusted for a positional reference.
    [InlineData("""{"shortlist":[{"position":2,"modelId":10,"variantId":21}]}""")]
    [InlineData("""{"shortlist":[{"position":0,"modelId":10,"variantId":21}]}""")]
    [InlineData("""{"shortlist":[{"position":1,"modelId":10,"variantId":21},{"position":1,"modelId":11,"variantId":25}]}""")]
    [InlineData("""{"shortlist":[{"position":1,"modelId":0,"variantId":21}]}""")]
    [InlineData("""{"shortlist":[{"position":1,"modelId":10,"variantId":-1}]}""")]
    [InlineData("""{"shortlist":[{"position":1,"modelId":10,"variantId":21},{"position":2,"modelId":10,"variantId":99}]}""")]
    [InlineData("""{"shortlist":[{"position":"first","modelId":10,"variantId":21}]}""")]
    // A stored budget that contradicts its own type could silently widen a later search, so it is
    // rejected as well.
    [InlineData("""{"lastFilters":{"budgetType":0,"budgetTarget":2500}}""")]
    [InlineData("""{"lastFilters":{"budgetType":2,"budgetTarget":null,"budgetMin":1000}}""")]
    [InlineData("""{"lastFilters":{"budgetType":2,"budgetTarget":0}}""")]
    [InlineData("""{"lastFilters":{"budgetType":3,"budgetMin":3000,"budgetMax":null}}""")]
    [InlineData("""{"lastFilters":{"budgetType":3,"budgetMin":4000,"budgetMax":3000}}""")]
    [InlineData("""{"lastFilters":{"budgetType":1,"budgetTarget":3000,"budgetMax":4000}}""")]
    public void A_structurally_invalid_stored_document_is_read_as_empty(string json)
    {
        Assert.Equal(ConversationStateDocument.Empty, ConversationStateDocument.Deserialize(json));
    }

    [Fact]
    public void A_stored_document_with_an_explicit_range_budget_still_round_trips()
    {
        var document = ConversationStateDocument.Empty with
        {
            LastFilters = new ConversationStateFilters
            {
                BudgetType = BudgetType.Range,
                BudgetMin = 2000,
                BudgetMax = 3000,
            },
        };

        var reloaded = ConversationStateDocument.Deserialize(document.ToJson());

        Assert.Equal(BudgetType.Range, reloaded.LastFilters!.BudgetType);
        Assert.Equal(2000, reloaded.LastFilters.BudgetMin);
        Assert.Equal(3000, reloaded.LastFilters.BudgetMax);
    }

    [Fact]
    public void An_unknown_commercial_fact_in_stored_json_is_ignored()
    {
        const string json = """
            {
              "shortlist": [{"position":1,"modelId":10,"variantId":21}],
              "lastModelId": 10,
              "lastVariantId": 21,
              "price": 2500,
              "quantity": 3,
              "workingHours": "10-8"
            }
            """;

        var reloaded = ConversationStateDocument.Deserialize(json);

        Assert.Equal(10, reloaded.LastModelId);
        Assert.Equal(21, reloaded.LastVariantId);
        Assert.DoesNotContain("price", reloaded.ToJson(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantity", reloaded.ToJson(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workingHours", reloaded.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_stored_shape_cannot_express_a_commercial_fact()
    {
        var storeableProperties = typeof(ConversationStateDocument)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(ConversationStateFilters).GetProperties().Select(property => property.Name))
            .Concat(typeof(ConversationShortlistEntry).GetProperties().Select(property => property.Name))
            .ToList();

        Assert.DoesNotContain(storeableProperties, name =>
            name.Contains("Price", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Stock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Availability", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Warranty", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Answer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Serializing_the_same_document_twice_produces_the_same_json()
    {
        var document = ConversationStateDocument.Empty with
        {
            Shortlist = ConversationStateDocument.BuildShortlist([(10, 21)]),
            LastIntent = "ProductSearch",
            LastFilters = new ConversationStateFilters { Brand = "Dell" },
        };

        Assert.Equal(document.ToJson(), document.ToJson());
    }
}
