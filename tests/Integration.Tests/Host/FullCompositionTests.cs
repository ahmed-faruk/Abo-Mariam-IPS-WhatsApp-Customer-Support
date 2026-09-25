using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Host;

/// <summary>
/// The one highest-seam full-composition test of docs/TECHNICAL.md section 36.3: a signed Meta webhook
/// through the real Host.Web composition, real PostgreSQL, the real Inbox and its worker, real
/// Conversations, Catalog and deterministic renderer, and the real durable Outbox and its worker. Only
/// the NLU and the Meta outbound sender are deterministic doubles.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed partial class FullCompositionTests(PostgresContainerFixture postgres)
{
    private const string Customer = "201000000001";
    private const string SearchText = "عايز ديل 24 IPS فيها HDMI ومش عايز أعدي 2500";
    private const string PriceText = "سعر الأولى كام؟";

    [Fact]
    public async Task F1_two_signed_turns_reply_twice_with_the_carried_reference_and_the_current_stored_price()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);

        Assert.Equal(DemoCli.Success, await ResetDemoAsync(connectionString));

        var nlu = new ScriptedAiNluClient()
            .With(SearchText, NluAnalysisResult.Success(new NluInterpretation
            {
                Intent = NluIntent.ProductSearch,
                Brand = "Dell",
                SizeInches = 24m,
                Panel = "IPS",
                RequiredPorts = ["HDMI"],
                Grades = [],
                BudgetType = NluBudgetType.Hard,
                BudgetTarget = 2500m,
            }))
            .With(PriceText, NluAnalysisResult.Success(new NluInterpretation
            {
                // The adversarial budget is a commercial number the model made up. It must never reach
                // the customer: the price comes from PostgreSQL through the carried reference.
                Intent = NluIntent.PriceCheck,
                Reference = "الأولى",
                RequiredPorts = [],
                Grades = [],
                BudgetType = NluBudgetType.Hard,
                BudgetTarget = 1111m,
            }));
        var sender = new RecordingOutboundSender();

        await using var factory = new ApplicationHostFactory(
            connectionString,
            StubOllamaTagsHandler.WithNames("qwen3.5:2b-q4_K_M"),
            services =>
            {
                services.AddSingleton<IAiNluClient>(nlu);
                services.AddSingleton<IOutboundMessageSender>(sender);
            });
        using var client = factory.CreateClient();
        var url = new Uri(client.BaseAddress!, "/api/whatsapp/webhook");
        var directory = Directory.CreateTempSubdirectory("full-composition-");

        try
        {
            var now = DateTimeOffset.UtcNow;

            await SendTurnAsync(client, url, directory, "turn-1.json", "wamid.FULL.1", SearchText, now);
            await WaitForRepliesAsync(database, sender, 1);

            var reply1 = sender.Sent[0].Body;
            var listed = ListedPrices().Matches(reply1);

            Assert.Contains("1. Dell P2419H - 2400 جنيه", reply1, StringComparison.Ordinal);
            Assert.NotEmpty(listed);
            Assert.All(listed, match => Assert.True(
                decimal.Parse(match.Groups["price"].Value, CultureInfo.InvariantCulture) <= 2500m,
                $"A listed price exceeds the hard budget: {match.Value}"));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var variantId = (await scope.ServiceProvider.GetRequiredService<ICatalogSearch>()
                    .FindByModelCodeAsync("P2419H"))!.VariantId;
                var outcome = await scope.ServiceProvider.GetRequiredService<ICatalogCommercialUpdates>()
                    .UpdatePriceAsync(new VariantPriceUpdate(variantId, 2350m, "test-operator"));

                Assert.Equal(CommercialUpdateOutcome.Updated, outcome);
            }

            await SendTurnAsync(client, url, directory, "turn-2.json", "wamid.FULL.2", PriceText, now.AddSeconds(1));
            await WaitForRepliesAsync(database, sender, 2);

            var reply2 = sender.Sent[1].Body;

            Assert.Equal("Dell P2419H: السعر الحالي 2350 جنيه.", reply2);
            Assert.DoesNotContain("1111", reply1, StringComparison.Ordinal);
            Assert.DoesNotContain("1111", reply2, StringComparison.Ordinal);
            Assert.DoesNotContain("2400", reply2, StringComparison.Ordinal);

            Assert.Equal(2, sender.Sent.Count);
            Assert.All(sender.Sent, message => Assert.Equal(Customer, message.CustomerExternalId));
            Assert.Equal([SearchText, PriceText], nlu.Messages);
            Assert.Equal(
                "2|2|2|0",
                await database.ScalarAsync(
                    "SELECT (SELECT count(*) FROM messaging.inbox_message) || '|' "
                    + "|| (SELECT count(*) FROM messaging.inbox_message WHERE processing_status = 'Processed') || '|' "
                    + "|| (SELECT count(*) FROM messaging.outbox_message WHERE delivery_status = 'Sent') || '|' "
                    + "|| (SELECT count(*) FROM messaging.outbox_message WHERE delivery_status <> 'Sent')"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<int> ResetDemoAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
                ["Catalog:Search:SizeToleranceInches"] = "0.5",
                ["Catalog:Search:SoftBudgetTolerance"] = "0.10",
            })
            .Build();

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        return await DemoCli.RunAsync(["reset", "--customer", Customer], configuration, output, error);
    }

    private static async Task SendTurnAsync(
        HttpClient client,
        Uri url,
        DirectoryInfo directory,
        string fileName,
        string providerMessageId,
        string text,
        DateTimeOffset timestamp)
    {
        var file = Path.Combine(directory.FullName, fileName);

        await WebhookCapture.CreateAsync(Customer, text, "123456789", providerMessageId, file, timestamp);

        Assert.Equal(HttpStatusCode.OK, await WebhookCapture.SendAsync(client, url, file, "test-app-secret"));
    }

    /// <summary>Waits until exactly the expected number of replies were sent and no queue work is open.</summary>
    private static async Task WaitForRepliesAsync(DatabaseCatalogReader database, RecordingOutboundSender sender, int expected)
    {
        var condition =
            "SELECT ((SELECT count(*) FROM messaging.outbox_message WHERE delivery_status = 'Sent') = "
            + expected.ToString(CultureInfo.InvariantCulture) + " "
            + "AND (SELECT count(*) FROM messaging.outbox_message WHERE delivery_status <> 'Sent') = 0 "
            + "AND (SELECT count(*) FROM messaging.inbox_message WHERE processing_status <> 'Processed') = 0)::text";
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (await database.ScalarAsync(condition) != "true" || sender.Sent.Count < expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"{expected} reply(ies) were not sent within 30 seconds; the sender saw {sender.Sent.Count}.");
            }

            await Task.Delay(100);
        }
    }

    [GeneratedRegex(@"^\d+\. .+ - (?<price>\d+(\.\d+)?) جنيه$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ListedPrices();
}
