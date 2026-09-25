using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Demo;

/// <summary>
/// The DemoOps seed, reset and verify commands, driven through the same entry point the operator
/// runs, over a real migrated PostgreSQL database. The commands must reproduce the frozen demo state
/// twice in a row, and verification must name every drift from it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DemoOperationsTests(PostgresContainerFixture postgres)
{
    private const string CustomerX = "201000000001";
    private const string CustomerY = "201000000002";
    private const string KeySku = "DEMO-P2419H-A";

    public static TheoryData<string[]> UsageErrors =>
    [
        [],
        ["unknown"],
        ["seed", "--customer", CustomerX],
        ["reset"],
        ["reset", "--customer"],
        ["reset", "--client", CustomerX],
        ["reset", "--customer", "+201000000001"],
        ["verify", "--customer", "1234567"],
        ["verify", "--customer", "2010000000011234"],
        ["verify", "--customer", "201000000001\n"],
        ["verify", "--customer", CustomerX, "--customer", CustomerX],
    ];

    [Fact]
    public async Task O1_seed_then_verify_passes_on_a_fresh_migrated_database()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var seed = await RunAsync(connectionString, "seed");
        var reseed = await RunAsync(connectionString, "seed");
        var verify = await RunAsync(connectionString, "verify", "--customer", CustomerX);

        AssertExit(DemoCli.Success, seed);
        Assert.Contains("SEED OK: catalogue SKUs present=25, business-info rows inserted=7", seed.Output);
        AssertExit(DemoCli.Success, reseed);
        Assert.Contains("SEED OK: catalogue SKUs present=25, business-info rows inserted=0", reseed.Output);

        AssertExit(DemoCli.Success, verify);
        Assert.Equal(
            [
                "PASS tolerances",
                "PASS catalogue",
                "PASS hard-budget-search",
                "PASS soft-budget-search",
                "PASS business-info",
                $"PASS customer {CustomerX}",
            ],
            Lines(verify, "PASS ", "FAIL "));
        Assert.Contains(
            "INFO hard-budget-search results: DEMO-P2419H-A 2400.00, DEMO-SE2419H-A 2150.00, DEMO-S2421H-B 2450.00",
            Lines(verify, "INFO "));
        Assert.Contains(
            "INFO soft-budget-search results: DEMO-P2422H-A 3100.00, DEMO-S2421H-A 2700.00, DEMO-P2419H-A 2400.00, "
            + "DEMO-SE2419H-A 2150.00, DEMO-U2419H-B 2950.00",
            Lines(verify, "INFO "));
        Assert.Equal(25, Lines(verify, "BASELINE catalogue ").Count);
        Assert.Contains(
            "BASELINE catalogue DEMO-P2419H-A | Dell P2419H | 23.8 in | IPS | 1920x1080 60Hz | "
            + "DisplayPort:1,HDMI:1,VGA:1 | grade A | price 2400.00 | quantity 3 | warranty 90 days",
            Lines(verify, "BASELINE catalogue "));
        Assert.Contains("BASELINE business-info ContactPhone | للتواصل: ٠١٠٩١٠٠٨٨١٥", Lines(verify, "BASELINE business-info "));
        Assert.Equal("VERIFY PASS", Lines(verify, "VERIFY ").Single());
    }

    [Fact]
    public async Task O2_reset_restores_every_mutation_and_is_repeatable()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "reset", "--customer", CustomerX));
        AssertExit(DemoCli.Success, await RunAsync(connectionString, "verify", "--customer", CustomerX));

        await MutateKeyVariantAsync(connectionString, price: 2350m, quantity: 0);
        await UpdateWorkingHoursAsync(connectionString, "test-only working hours");
        await AddCustomerActivityAsync(connectionString, CustomerX);

        var drifted = await RunAsync(connectionString, "verify", "--customer", CustomerX);

        AssertExit(DemoCli.VerifyFailed, drifted);
        var failures = Lines(drifted, "FAIL ");
        Assert.Contains($"FAIL catalogue: {KeySku} price=2350.00 (expected 2400.00)", failures);
        Assert.Contains($"FAIL catalogue: {KeySku} quantity=0 (expected 3)", failures);
        Assert.Contains($"FAIL catalogue: {KeySku} available=False (expected True)", failures);
        Assert.Contains("FAIL business-info: WorkingHours differs from the approved value", failures);
        Assert.Contains($"FAIL customer {CustomerX}: conversations=1 inbox=1 outbox=1 (expected 0 each)", failures);
        Assert.Equal("VERIFY FAIL: 4 check(s) failed", Lines(drifted, "VERIFY ").Single());

        var restore = await RunAsync(connectionString, "reset", "--customer", CustomerX);

        AssertExit(DemoCli.Success, restore);
        Assert.Equal(
            [
                "RESET messaging: inbox=1 outbox=1 envelopes=1",
                "RESET conversations: customers=1",
                "RESET catalogue: variants=25 changed values=2",
                "RESET business-info: keys=7 changed=1",
                "RESET OK",
            ],
            Lines(restore, "RESET "));
        AssertExit(DemoCli.Success, await RunAsync(connectionString, "verify", "--customer", CustomerX));

        var again = await RunAsync(connectionString, "reset", "--customer", CustomerX);

        AssertExit(DemoCli.Success, again);
        Assert.Equal(
            [
                "RESET messaging: inbox=0 outbox=0 envelopes=0",
                "RESET conversations: customers=0",
                "RESET catalogue: variants=25 changed values=0",
                "RESET business-info: keys=7 changed=0",
                "RESET OK",
            ],
            Lines(again, "RESET "));
        AssertExit(DemoCli.Success, await RunAsync(connectionString, "verify", "--customer", CustomerX));

        Assert.Equal(
            "UpdatePrice,UpdateQuantity",
            await database.ScalarAsync(
                "SELECT string_agg(action, ',' ORDER BY action) FROM catalog.audit_log WHERE user_id = 'demo-reset'"));
        Assert.Equal(
            "2400.00|3",
            await database.ScalarAsync(
                $"SELECT selling_price || '|' || quantity FROM catalog.product_variant WHERE sku = '{KeySku}'"));
    }

    [Fact]
    public async Task O3_reset_refuses_while_customer_work_is_open_and_changes_nothing()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "reset", "--customer", CustomerX));
        await MutateKeyVariantAsync(connectionString, price: 2350m, quantity: 3);
        await AddCustomerActivityAsync(connectionString, CustomerX, terminal: false);

        var refused = await RunAsync(connectionString, "reset", "--customer", CustomerX);

        AssertExit(DemoCli.ResetRefused, refused);
        Assert.StartsWith("RESET REFUSED:", Lines(refused, "RESET ").Single(), StringComparison.Ordinal);
        Assert.Equal(
            "2350.00",
            await database.ScalarAsync($"SELECT selling_price FROM catalog.product_variant WHERE sku = '{KeySku}'"));
        Assert.Equal(
            "1|1|1",
            await database.ScalarAsync(
                $"SELECT (SELECT count(*) FROM messaging.inbox_message WHERE customer_external_id = '{CustomerX}') "
                + $"|| '|' || (SELECT count(*) FROM messaging.outbox_message WHERE customer_external_id = '{CustomerX}') "
                + $"|| '|' || (SELECT count(*) FROM conversations.customer WHERE whatsapp_number = '{CustomerX}')"));
    }

    [Fact]
    public async Task O4_reset_of_one_customer_keeps_the_other_customers_rows()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "seed"));
        await AddCustomerActivityAsync(connectionString, CustomerX);
        await AddCustomerActivityAsync(connectionString, CustomerY);

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "reset", "--customer", CustomerX));
        AssertExit(DemoCli.Success, await RunAsync(connectionString, "verify", "--customer", CustomerX));

        var other = await RunAsync(connectionString, "verify", "--customer", CustomerY);

        AssertExit(DemoCli.VerifyFailed, other);
        Assert.Equal(
            [$"FAIL customer {CustomerY}: conversations=1 inbox=1 outbox=1 (expected 0 each)"],
            Lines(other, "FAIL "));
    }

    [Fact]
    public async Task O5_seeded_working_hours_is_read_and_updated_through_the_storefront_contracts()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "seed"));

        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: false);

        Assert.Equal(
            "مواعيدنا من السبت للخميس من 11 الصبح لحد 10 بالليل، والجمعة من 2 الضهر لحد 10 بالليل.",
            (await ReadWorkingHoursAsync(provider))?.AnswerAr);

        await UpdateWorkingHoursAsync(connectionString, "مواعيدنا الجديدة للاختبار فقط.");

        Assert.Equal("مواعيدنا الجديدة للاختبار فقط.", (await ReadWorkingHoursAsync(provider))?.AnswerAr);
    }

    [Fact]
    public void O6_dataset_is_the_frozen_controlled_demo_dataset()
    {
        var models = DemoDataset.Catalogue;
        var variants = models.SelectMany(model => model.Variants.Select(variant => (Model: model, Variant: variant))).ToList();

        Assert.Equal(20, models.Count);
        Assert.Equal(25, variants.Count);
        Assert.Equal(models.Count, models.Select(model => model.ModelCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(variants.Count, variants.Select(pair => pair.Variant.Sku).Distinct(StringComparer.Ordinal).Count());

        Assert.All(models, model =>
        {
            Assert.InRange(model.SizeInches, 10m, 60m);
            Assert.Contains(model.PanelType, (string[])["IPS", "TN", "VA", "OLED", "Other"]);
            Assert.InRange(model.RefreshRate, 24, 500);
            Assert.True(model.ResolutionWidth > 0 && model.ResolutionHeight > 0);
            Assert.NotEmpty(model.Ports);
            Assert.All(model.Ports, port => Assert.InRange(port.Count, 1, 16));
            Assert.Equal($"{model.Brand} {model.Model}", model.DisplayName);
        });
        Assert.All(variants, pair =>
        {
            Assert.Equal(DemoDataset.Sku(pair.Model.ModelCode, pair.Variant.Grade), pair.Variant.Sku);
            Assert.Contains(pair.Variant.Grade, (string[])["A", "B", "C"]);
            Assert.True(pair.Variant.SellingPrice >= 0m);
            Assert.True(pair.Variant.Quantity > 0);
            Assert.True(pair.Variant.WarrantyDays >= 0);
        });

        var key = Assert.Single(models, model => model.ModelCode == DemoDataset.KeyModelCode);
        Assert.Equal(
            "Dell|Dell P2419H|23.8|IPS|1920x1080|60",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{key.Brand}|{key.DisplayName}|{key.SizeInches}|{key.PanelType}|{key.ResolutionWidth}x{key.ResolutionHeight}|{key.RefreshRate}"));
        Assert.Equal(
            ["DisplayPort", "HDMI", "VGA"],
            key.Ports.Select(port => port.PortType).Order(StringComparer.Ordinal));
        var keyVariant = Assert.Single(key.Variants);
        Assert.Equal((KeySku, "A", 2400m, 3), (keyVariant.Sku, keyVariant.Grade, keyVariant.SellingPrice, keyVariant.Quantity));

        Assert.Equal(BusinessInfoKeyNames.All, DemoDataset.BusinessInfo.Select(row => row.Key));
        Assert.All(DemoDataset.BusinessInfo, row => Assert.True(row.AnswerEn is null && row.IsActive));
        Assert.Equal(
            [
                "مواعيدنا من السبت للخميس من 11 الصبح لحد 10 بالليل، والجمعة من 2 الضهر لحد 10 بالليل.",
                "المحل في وسط البلد، القاهرة. ابعتلنا وهنبعتلك اللوكيشن.",
                "بنوصّل لكل محافظات مصر، ومصاريف الشحن بتتحدد حسب المحافظة.",
                "بنقبل كاش، وإنستاباي، وفودافون كاش.",
                "كل شاشة عليها ضمان حسب الفرز، والتفاصيل مكتوبة مع كل منتج.",
                "للتواصل: ٠١٠٩١٠٠٨٨١٥",
                "الاسترجاع أو الاستبدال خلال 3 أيام من الاستلام لو الشاشة بنفس حالتها.",
            ],
            DemoDataset.BusinessInfo.Select(row => row.AnswerAr));

        Assert.Equal(0.5m, DemoDataset.DemoSizeToleranceInches);
        Assert.Equal(0.10m, DemoDataset.DemoSoftBudgetTolerance);
    }

    [Fact]
    public async Task O7_verify_fails_when_the_size_tolerance_is_not_the_demo_value()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        AssertExit(DemoCli.Success, await RunAsync(connectionString, "seed"));

        var verify = await RunAsync(connectionString, "0.4", ["verify", "--customer", CustomerX]);

        AssertExit(DemoCli.VerifyFailed, verify);
        Assert.Equal(["FAIL tolerances: SizeToleranceInches=0.4 (expected 0.5)"], Lines(verify, "FAIL "));
    }

    [Theory]
    [MemberData(nameof(UsageErrors))]
    public async Task O8_invalid_arguments_are_a_usage_error_before_anything_runs(string[] args)
    {
        var run = await RunAsync("Host=127.0.0.1;Port=1;Database=never_used;Username=nobody", "0.5", args);

        AssertExit(DemoCli.UsageError, run);
        Assert.StartsWith("USAGE ERROR:", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task O9_missing_connection_string_is_an_unexpected_error()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await DemoCli.RunAsync(["seed"], configuration, output, error);

        Assert.Equal(DemoCli.UnexpectedError, exitCode);
        Assert.StartsWith("ERROR: InvalidOperationException:", error.ToString(), StringComparison.Ordinal);
    }

    private static Task<CliRun> RunAsync(string connectionString, params string[] args) =>
        RunAsync(connectionString, "0.5", args);

    private static async Task<CliRun> RunAsync(string connectionString, string sizeTolerance, string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
                ["Catalog:Search:SizeToleranceInches"] = sizeTolerance,
                ["Catalog:Search:SoftBudgetTolerance"] = "0.10",
            })
            .Build();

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await DemoCli.RunAsync(args, configuration, output, error);

        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    private static void AssertExit(int expected, CliRun run)
    {
        if (run.ExitCode != expected)
        {
            Assert.Fail(
                $"Expected exit code {expected} but got {run.ExitCode}.{Environment.NewLine}"
                + $"OUTPUT:{Environment.NewLine}{run.Output}{Environment.NewLine}ERROR:{Environment.NewLine}{run.Error}");
        }
    }

    private static List<string> Lines(CliRun run, params string[] prefixes) =>
    [
        .. run.Output
            .Split(Environment.NewLine)
            .Where(line => prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal))),
    ];

    private static async Task MutateKeyVariantAsync(string connectionString, decimal price, int quantity)
    {
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);
        await using var scope = provider.CreateAsyncScope();

        var ids = await scope.ServiceProvider.GetRequiredService<IDemoCatalogData>().FindVariantIdsAsync([KeySku]);
        var updates = scope.ServiceProvider.GetRequiredService<ICatalogCommercialUpdates>();

        await updates.UpdatePriceAsync(new VariantPriceUpdate(ids[KeySku], price, "test-operator"));
        await updates.UpdateQuantityAsync(new VariantQuantityUpdate(ids[KeySku], quantity, "test-operator"));
    }

    private static async Task UpdateWorkingHoursAsync(string connectionString, string answerAr)
    {
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: false);
        await using var scope = provider.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<IStorefrontBusinessInfoUpdates>()
            .UpdateAsync(new BusinessInfoUpdate(BusinessInfoKeyNames.WorkingHours, answerAr, null, true));

        Assert.Equal(BusinessInfoUpdateOutcome.Updated, outcome);
    }

    private static async Task<BusinessInfoValue?> ReadWorkingHoursAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IStorefrontBusinessInfo>()
            .GetByKeyAsync(BusinessInfoKeyNames.WorkingHours);
    }

    /// <summary>
    /// Gives a customer the state one demo run leaves behind: a Human-mode conversation with carried
    /// state, one inbound message and one reply. Terminal work is Processed and Sent; open work stays
    /// Pending, the way a message the workers have not reached yet would.
    /// </summary>
    private static async Task AddCustomerActivityAsync(string connectionString, string customer, bool terminal = true)
    {
        var database = new DatabaseCatalogReader(connectionString);

        await database.ExecuteAsync(
            $"INSERT INTO conversations.customer (whatsapp_number) VALUES ('{customer}');"
            + "INSERT INTO conversations.conversation (customer_id, mode) "
            + $"SELECT id, 'Human' FROM conversations.customer WHERE whatsapp_number = '{customer}';"
            + "INSERT INTO conversations.conversation_state (conversation_id, state_json, expires_at) "
            + "SELECT c.id, '{\"LastModelId\": 1}'::jsonb, now() + interval '30 minutes' "
            + "FROM conversations.conversation c JOIN conversations.customer cu ON cu.id = c.customer_id "
            + $"WHERE cu.whatsapp_number = '{customer}';");

        await using (var provider = DemoDataHost.Build(connectionString, includeDemoData: false))
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>().EnqueueAsync(new InboundMessageEnvelope(
                RawBody: $"{{\"demo\":\"{customer}\"}}",
                ProviderMessageId: $"wamid.{customer}",
                CustomerExternalId: customer,
                MessageType: "text",
                ProviderTimestamp: new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                Body: "hello"));
            await scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>().EnqueueAsync(
                new OutboundMessageRequest(1, customer, $"corr-{customer}", "AI", "reply"));
        }

        if (terminal)
        {
            await database.ExecuteAsync(
                "UPDATE messaging.inbox_message SET processing_status = 'Processed', processed_at = now() "
                + $"WHERE customer_external_id = '{customer}';"
                + "UPDATE messaging.outbox_message SET delivery_status = 'Sent', sent_at = now(), "
                + $"provider_message_id = 'wamid.out-{customer}' WHERE customer_external_id = '{customer}';");
        }
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
