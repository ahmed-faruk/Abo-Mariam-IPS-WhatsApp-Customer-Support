using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>
/// A filter has to come back to the spelling the schema allows, otherwise a correct request such as
/// "other" or "grade a" would silently match nothing.
/// </summary>
public sealed class PanelAndGradeTests
{
    [Theory]
    [InlineData("IPS", "IPS")]
    [InlineData("ips", "IPS")]
    [InlineData("  ips  ", "IPS")]
    [InlineData("tn", "TN")]
    [InlineData("va", "VA")]
    [InlineData("oled", "OLED")]
    [InlineData("other", "Other")]
    [InlineData("OTHER", "Other")]
    public void A_known_panel_type_returns_the_stored_spelling(string value, string expected)
    {
        Assert.Equal(expected, PanelTypes.Canonicalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_panel_type_is_no_filter(string? value)
    {
        Assert.Null(PanelTypes.Canonicalize(value));
    }

    [Fact]
    public void An_unknown_panel_type_stays_normalized_so_it_matches_nothing()
    {
        Assert.Equal("led", PanelTypes.Canonicalize("LED"));
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("a", "A")]
    [InlineData(" a ", "A")]
    [InlineData("b", "B")]
    [InlineData("c", "C")]
    public void A_known_grade_returns_the_stored_spelling(string value, string expected)
    {
        Assert.Equal(expected, Grades.Canonicalize(value));
    }

    [Fact]
    public void Accepted_grades_are_canonicalized_deduplicated_and_keep_their_order()
    {
        Assert.Equal(["A", "B"], Grades.CanonicalizeAll(["a", "B", "A", " b ", ""]));
    }

    [Fact]
    public void An_empty_grade_list_accepts_every_grade()
    {
        Assert.Empty(Grades.CanonicalizeAll(null));
        Assert.Empty(Grades.CanonicalizeAll([]));
        Assert.Empty(Grades.CanonicalizeAll(["  "]));
    }
}
