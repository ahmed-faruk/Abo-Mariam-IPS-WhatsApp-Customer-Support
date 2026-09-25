using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Admin;
using WhatsAppMonitorAssistant.Integration.Tests.Host;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// The Admin Lite conversation list and transcript on reset-controlled demo data: after the documented
/// reset the demo customer has one conversation, every inbound message of that customer appears in the
/// transcript (including the first one, whose provider timestamp precedes the conversation start), and
/// the order is deterministic by timestamp plus row id.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminLiteConversationTests(PostgresContainerFixture postgres)
{
    private const string Customer = "201000000001";

    [Fact]
    public async Task T1_the_reset_customer_has_one_conversation_with_every_inbound_message_in_deterministic_order()
    {
        var sender = new RecordingOutboundSender();
        await using var factory = await StartAsync(sender, "m1", "m2", "m3");
        var now = DateTime.UtcNow;

        await EnqueueAsync(factory, "wamid.T1.1", "m1", now.AddMinutes(-10));
        await EnqueueAsync(factory, "wamid.T1.2", "m2", now.AddMinutes(-5));
        await EnqueueAsync(factory, "wamid.T1.3", "m3", now.AddMinutes(-5));
        await WaitForRepliesAsync(sender, 3);

        await using var scope = factory.Services.CreateAsyncScope();
        var conversation = Assert.Single(await scope.ServiceProvider.GetRequiredService<IConversationAdminReads>().ListAsync());
        var reads = scope.ServiceProvider.GetRequiredService<IMessagingTranscriptReads>();
        var inbound = await reads.ListInboundByCustomerAsync(Customer);
        var outbound = await reads.ListOutboundByConversationAsync(conversation.ConversationId);

        Assert.Equal(Customer, conversation.CustomerExternalId);
        Assert.Equal(["wamid.T1.1", "wamid.T1.2", "wamid.T1.3"], inbound.Select(message => message.ProviderMessageId));
        Assert.True(inbound[1].InboxMessageId < inbound[2].InboxMessageId);
        Assert.True(inbound[0].ProviderTimestamp < conversation.StartedAt);
        Assert.Equal(3, outbound.Count);

        var lines = AdminTranscriptComposer.Compose(inbound, outbound);

        Assert.Equal(["m1", "m2", "m3"], lines.Take(3).Select(line => line.Text));
        Assert.Equal([true, true, true, false, false, false], lines.Select(line => line.Inbound));
        Assert.Equal(lines, AdminTranscriptComposer.Compose(inbound, outbound));
    }

    [Fact]
    public async Task T2_the_detail_page_shows_the_mode_and_encodes_the_message_text()
    {
        var sender = new RecordingOutboundSender();
        await using var factory = await StartAsync(sender, "<b>x</b>");

        await EnqueueAsync(factory, "wamid.T2.1", "<b>x</b>", DateTime.UtcNow);
        await WaitForRepliesAsync(sender, 1);

        long conversationId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            conversationId = Assert.Single(await scope.ServiceProvider.GetRequiredService<IConversationAdminReads>().ListAsync()).ConversationId;
        }

        using var client = factory.AdminClient();
        var list = await client.GetStringAsync("/admin/conversations");
        var detail = await client.GetStringAsync($"/admin/conversations/{conversationId}");

        Assert.Contains($"/admin/conversations/{conversationId}", list, StringComparison.Ordinal);
        Assert.Contains("<strong id=\"mode\">Ai</strong>", detail, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>x</b>", detail, StringComparison.Ordinal);
        Assert.Contains(sender.Sent[0].Body.Split('\n')[0], detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T3_an_unknown_conversation_is_not_found()
    {
        await using var factory = await StartAsync(new RecordingOutboundSender());
        using var client = factory.AdminClient();

        using var response = await client.GetAsync("/admin/conversations/987654321");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<AdminLiteHostFactory> StartAsync(RecordingOutboundSender sender, params string[] greetings)
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        Assert.Equal(DemoCli.Success, await AdminForms.RunDemoOpsAsync(connectionString, "reset", "--customer", Customer));

        var nlu = new ScriptedAiNluClient();

        foreach (var greeting in greetings)
        {
            nlu.With(greeting, NluAnalysisResult.Success(new NluInterpretation
            {
                Intent = NluIntent.Greeting,
                RequiredPorts = [],
                Grades = [],
                BudgetType = NluBudgetType.None,
            }));
        }

        var factory = new AdminLiteHostFactory(
            connectionString,
            configureServices: services =>
            {
                services.AddSingleton<IAiNluClient>(nlu);
                services.AddSingleton<IOutboundMessageSender>(sender);
            });
        factory.StartServer();

        return factory;
    }

    private static async Task EnqueueAsync(AdminLiteHostFactory factory, string providerMessageId, string body, DateTime timestamp)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>().EnqueueAsync(new InboundMessageEnvelope(
            RawBody: $"{{\"id\":\"{providerMessageId}\"}}",
            ProviderMessageId: providerMessageId,
            CustomerExternalId: Customer,
            MessageType: "text",
            ProviderTimestamp: timestamp,
            Body: body));
    }

    private static async Task WaitForRepliesAsync(RecordingOutboundSender sender, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (sender.Sent.Count < expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Only {sender.Sent.Count} of {expected} replies were sent within 30 seconds.");
            }

            await Task.Delay(100);
        }

        // The worker records the delivery right after the sender returns.
        await Task.Delay(500);
    }
}
