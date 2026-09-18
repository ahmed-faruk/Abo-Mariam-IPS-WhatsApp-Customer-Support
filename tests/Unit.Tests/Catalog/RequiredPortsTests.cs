using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Catalog;

/// <summary>Required ports are a set: asking twice for one port does not demand two of them.</summary>
public sealed class RequiredPortsTests
{
    [Fact]
    public void Ports_are_normalized_like_the_stored_values_they_are_compared_with()
    {
        Assert.Equal(["hdmi", "displayport"], RequiredPorts.Normalize(["HDMI", " DisplayPort "]));
    }

    [Fact]
    public void Repeated_ports_collapse_to_one_required_type()
    {
        Assert.Equal(["hdmi"], RequiredPorts.Normalize(["HDMI", "hdmi", " hdmi "]));
    }

    [Fact]
    public void Port_order_is_preserved_so_the_criteria_stay_deterministic()
    {
        Assert.Equal(["displayport", "hdmi"], RequiredPorts.Normalize(["DisplayPort", "HDMI"]));
    }

    [Fact]
    public void Blank_ports_are_dropped_instead_of_requiring_an_empty_type()
    {
        Assert.Equal(["hdmi"], RequiredPorts.Normalize(["", "  ", "HDMI"]));
    }

    [Fact]
    public void No_requested_ports_means_no_port_constraint()
    {
        Assert.Empty(RequiredPorts.Normalize(null));
        Assert.Empty(RequiredPorts.Normalize([]));
    }
}
