using WhatsAppMonitorAssistant.Host.Web.Admin;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>The transcript merge order against an independently written expected sequence.</summary>
public sealed class AdminTranscriptComposerTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Merge_orders_by_time_then_inbound_before_outbound_then_row_id()
    {
        IReadOnlyList<InboundTranscriptEntry> inbound =
        [
            new(InboxMessageId: 12, "wamid.c", "text", "third inbound, same time as the second", T0.AddMinutes(5)),
            new(InboxMessageId: 11, "wamid.b", "text", "second inbound", T0.AddMinutes(5)),
            new(InboxMessageId: 10, "wamid.a", "image", null, T0),
        ];
        IReadOnlyList<OutboundTranscriptEntry> outbound =
        [
            new(OutboxMessageId: 21, "AI", "reply at the same instant as inbound b", "Sent", T0.AddMinutes(5)),
            new(OutboxMessageId: 20, "AI", "first reply", "Sent", T0.AddMinutes(1)),
        ];

        var lines = AdminTranscriptComposer.Compose(inbound, outbound);

        Assert.Equal(
            [
                (true, "[image]"),
                (false, "first reply"),
                (true, "second inbound"),
                (true, "third inbound, same time as the second"),
                (false, "reply at the same instant as inbound b"),
            ],
            lines.Select(line => (line.Inbound, line.Text)));
        Assert.Equal("AI · Sent", lines[1].Detail);
        Assert.Equal("image", lines[0].Detail);
    }
}
