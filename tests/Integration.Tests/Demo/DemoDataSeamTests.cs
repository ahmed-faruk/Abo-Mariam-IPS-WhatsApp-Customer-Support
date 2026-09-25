using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Host;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Demo;

/// <summary>
/// The module-owned demo-data seams of docs/TECHNICAL.md section 36.5, over a real migrated PostgreSQL
/// database. The seeds only insert, the resets only remove the named demo customers, and the
/// production composition never exposes any of these mutation contracts.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DemoDataSeamTests(PostgresContainerFixture postgres)
{
    private const string CustomerX = "201000000001";
    private const string CustomerY = "201000000002";

    [Fact]
    public async Task D1_catalog_seed_inserts_missing_rows_once_and_keeps_a_changed_price()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        var first = await InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(CatalogSeed()));

        Assert.Equal(["DEMO-T1-A", "DEMO-T1-B", "DEMO-T2-A"], first.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("2", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model"));
        Assert.Equal("3", await database.ScalarAsync("SELECT count(*) FROM catalog.product_variant"));
        Assert.Equal("5", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model_port"));
        Assert.Equal("0", await database.ScalarAsync("SELECT count(*) FROM catalog.audit_log"));
        Assert.Equal(
            "Demo T1 24|23.8|IPS|1920x1080@60|Office,Programming|HDMI:2,VGA:1",
            await database.ScalarAsync(
                "SELECT m.display_name || '|' || m.size_inches || '|' || m.panel_type || '|' "
                + "|| m.resolution_width || 'x' || m.resolution_height || '@' || m.refresh_rate || '|' "
                + "|| array_to_string(m.search_tags, ',') || '|' "
                + "|| (SELECT string_agg(p.port_type || ':' || p.count, ',' ORDER BY p.port_type) "
                + "    FROM catalog.product_model_port p WHERE p.product_model_id = m.id) "
                + "FROM catalog.product_model m WHERE m.model_code = 'DEMO-T1'"));
        Assert.Equal(
            "A|2400.00|3|90|true",
            await database.ScalarAsync(
                "SELECT grade || '|' || selling_price || '|' || quantity || '|' || warranty_days || '|' || is_active "
                + "FROM catalog.product_variant WHERE sku = 'DEMO-T1-A'"));

        var outcome = await InScopeAsync(provider, (ICatalogCommercialUpdates updates) =>
            updates.UpdatePriceAsync(new VariantPriceUpdate(first["DEMO-T1-A"], 2350m, "test-operator")));
        Assert.Equal(CommercialUpdateOutcome.Updated, outcome);

        var second = await InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(CatalogSeed()));

        Assert.Equal(Ordered(first), Ordered(second));
        Assert.Equal("2", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model"));
        Assert.Equal("3", await database.ScalarAsync("SELECT count(*) FROM catalog.product_variant"));
        Assert.Equal("5", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model_port"));
        Assert.Equal(
            "2350.00",
            await database.ScalarAsync("SELECT selling_price FROM catalog.product_variant WHERE sku = 'DEMO-T1-A'"));
        Assert.Equal("1", await database.ScalarAsync("SELECT count(*) FROM catalog.audit_log"));
    }

    [Fact]
    public async Task D1_catalog_seed_adds_a_missing_variant_to_an_existing_model()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        var first = await InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(CatalogSeed()));
        var extended = await InScopeAsync(provider, (IDemoCatalogData catalog) =>
            catalog.EnsureSeedAsync(CatalogSeed(withSecondT2Variant: true)));

        Assert.Equal(4, extended.Count);
        Assert.All(first, pair => Assert.Equal(pair.Value, extended[pair.Key]));
        Assert.Equal("2", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model"));
        Assert.Equal(
            "DEMO-T2",
            await database.ScalarAsync(
                "SELECT m.model_code FROM catalog.product_variant v "
                + "JOIN catalog.product_model m ON m.id = v.product_model_id WHERE v.sku = 'DEMO-T2-B'"));

        var found = await InScopeAsync(provider, (IDemoCatalogData catalog) =>
            catalog.FindVariantIdsAsync(["DEMO-T2-B", "DEMO-MISSING"]));

        Assert.Equal([new KeyValuePair<string, long>("DEMO-T2-B", extended["DEMO-T2-B"])], found);
    }

    [Fact]
    public async Task D1_catalog_seed_rejects_an_inconsistent_seed_before_writing()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        var duplicateSku = CatalogSeed()
            .Append(Model("DEMO-T3", Variant("DEMO-T1-A", "A", 1000m)))
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(duplicateSku)));
        Assert.Equal("0", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model"));

        await InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(CatalogSeed()));

        IReadOnlyList<DemoModelSeed> movedSku = [Model("DEMO-T3", Variant("DEMO-T1-A", "A", 1000m))];

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InScopeAsync(provider, (IDemoCatalogData catalog) => catalog.EnsureSeedAsync(movedSku)));
        Assert.Equal("2", await database.ScalarAsync("SELECT count(*) FROM catalog.product_model"));
        Assert.Equal("3", await database.ScalarAsync("SELECT count(*) FROM catalog.product_variant"));
    }

    [Fact]
    public async Task D2_business_info_seed_inserts_missing_keys_once_and_never_overwrites()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        var preexisting = await InScopeAsync(provider, (IDemoBusinessInfoData seed) =>
            seed.EnsureSeedAsync([new BusinessInfoUpdate(BusinessInfoKeyNames.Address, "pre-Address", null, true)]));
        var first = await InScopeAsync(provider, (IDemoBusinessInfoData seed) => seed.EnsureSeedAsync(AllBusinessInfo()));

        Assert.Equal(1, preexisting);
        Assert.Equal(6, first);
        Assert.Equal("7", await database.ScalarAsync("SELECT count(*) FROM storefront.business_info"));
        Assert.Equal("pre-Address", (await ReadBusinessInfoAsync(provider, BusinessInfoKeyNames.Address))?.AnswerAr);
        Assert.Equal(
            "seed-WorkingHours",
            (await ReadBusinessInfoAsync(provider, BusinessInfoKeyNames.WorkingHours))?.AnswerAr);

        var edit = await InScopeAsync(provider, (IStorefrontBusinessInfoUpdates updates) =>
            updates.UpdateAsync(new BusinessInfoUpdate(BusinessInfoKeyNames.WorkingHours, "edited-WorkingHours", null, true)));
        Assert.Equal(BusinessInfoUpdateOutcome.Updated, edit);

        var second = await InScopeAsync(provider, (IDemoBusinessInfoData seed) => seed.EnsureSeedAsync(AllBusinessInfo()));

        Assert.Equal(0, second);
        Assert.Equal("7", await database.ScalarAsync("SELECT count(*) FROM storefront.business_info"));
        Assert.Equal(
            "edited-WorkingHours",
            (await ReadBusinessInfoAsync(provider, BusinessInfoKeyNames.WorkingHours))?.AnswerAr);
    }

    [Fact]
    public async Task D2_business_info_seed_rejects_an_invalid_row_before_writing()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        IReadOnlyList<BusinessInfoUpdate> unapprovedKey =
        [
            new(BusinessInfoKeyNames.WorkingHours, "seed-WorkingHours", null, true),
            new("OpeningDays", "seed-OpeningDays", null, true),
        ];
        IReadOnlyList<BusinessInfoUpdate> blankAnswer =
        [
            new(BusinessInfoKeyNames.WorkingHours, "seed-WorkingHours", null, true),
            new(BusinessInfoKeyNames.Address, "   ", null, true),
        ];

        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScopeAsync(provider, (IDemoBusinessInfoData seed) => seed.EnsureSeedAsync(unapprovedKey)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScopeAsync(provider, (IDemoBusinessInfoData seed) => seed.EnsureSeedAsync(blankAnswer)));

        Assert.Equal("0", await database.ScalarAsync("SELECT count(*) FROM storefront.business_info"));
    }

    [Fact]
    public async Task D3_conversation_reset_removes_only_the_named_customer_with_its_conversations_and_state()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        await database.ExecuteAsync(
            $"INSERT INTO conversations.customer (whatsapp_number) VALUES ('{CustomerX}'), ('{CustomerY}');"
            + "INSERT INTO conversations.conversation (customer_id, mode, closed_at) "
            + $"SELECT id, 'Closed', now() FROM conversations.customer WHERE whatsapp_number = '{CustomerX}';"
            + "INSERT INTO conversations.conversation (customer_id, mode) "
            + $"SELECT id, 'Human' FROM conversations.customer WHERE whatsapp_number = '{CustomerX}';"
            + "INSERT INTO conversations.conversation (customer_id, mode) "
            + $"SELECT id, 'AI' FROM conversations.customer WHERE whatsapp_number = '{CustomerY}';"
            + "INSERT INTO conversations.conversation_state (conversation_id, state_json, expires_at) "
            + "SELECT id, '{\"LastModelId\": 1}'::jsonb, now() + interval '30 minutes' "
            + "FROM conversations.conversation WHERE mode <> 'Closed';");

        Assert.Equal(2, await InScopeAsync(provider, (IDemoConversationData data) => data.CountConversationsAsync([CustomerX])));
        Assert.Equal(1, await InScopeAsync(provider, (IDemoConversationData data) => data.CountConversationsAsync([CustomerY])));

        var deleted = await InScopeAsync(provider, (IDemoConversationData data) => data.DeleteCustomersAsync([CustomerX]));

        Assert.Equal(1, deleted);
        Assert.Equal(0, await InScopeAsync(provider, (IDemoConversationData data) => data.CountConversationsAsync([CustomerX])));
        Assert.Equal(
            CustomerY,
            await database.ScalarAsync("SELECT string_agg(whatsapp_number, ',') FROM conversations.customer"));
        Assert.Equal("1", await database.ScalarAsync("SELECT count(*) FROM conversations.conversation"));
        Assert.Equal(
            "1|AI",
            await database.ScalarAsync(
                "SELECT count(*) || '|' || min(c.mode) FROM conversations.conversation_state s "
                + "JOIN conversations.conversation c ON c.id = s.conversation_id "
                + $"JOIN conversations.customer cu ON cu.id = c.customer_id WHERE cu.whatsapp_number = '{CustomerY}'"));
        Assert.Equal("1", await database.ScalarAsync("SELECT count(*) FROM conversations.conversation_state"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScopeAsync(provider, (IDemoConversationData data) => data.DeleteCustomersAsync([])));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScopeAsync(provider, (IDemoConversationData data) => data.DeleteCustomersAsync(["  "])));
        Assert.Equal("1", await database.ScalarAsync("SELECT count(*) FROM conversations.customer"));
    }

    [Fact]
    public async Task D4_messaging_reset_removes_the_named_customer_rows_and_only_orphaned_envelopes()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        await SeedMessagingAsync(provider);
        await database.ExecuteAsync(
            "UPDATE messaging.inbox_message SET processing_status = 'Processed', processed_at = now();"
            + "UPDATE messaging.inbox_message SET processing_status = 'DeadLettered' WHERE provider_message_id = 'wamid.x1';"
            + "UPDATE messaging.outbox_message SET delivery_status = 'Sent', sent_at = now(), "
            + "provider_message_id = 'wamid.out-' || id;");

        Assert.Equal(new DemoMessagingCounts(2, 1, 0), await CountMessagingAsync(provider, CustomerX));
        Assert.Equal("3", await database.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));

        var result = await InScopeAsync(provider, (IDemoMessagingData data) => data.DeleteAsync([CustomerX]));

        Assert.Equal(new DemoMessagingDeleteResult(Refused: false, Inbox: 2, Outbox: 1, Envelopes: 1), result);
        Assert.Equal(new DemoMessagingCounts(0, 0, 0), await CountMessagingAsync(provider, CustomerX));
        Assert.Equal(new DemoMessagingCounts(2, 1, 0), await CountMessagingAsync(provider, CustomerY));
        Assert.Equal(
            "wamid.y1,wamid.y2",
            await database.ScalarAsync(
                "SELECT string_agg(provider_message_id, ',' ORDER BY provider_message_id) FROM messaging.inbox_message"));
        Assert.Equal(
            "corr-y",
            await database.ScalarAsync("SELECT string_agg(correlation_id, ',') FROM messaging.outbox_message"));
        Assert.Equal(
            "shared,y-only",
            await database.ScalarAsync(
                "SELECT string_agg(raw_body->>'demo', ',' ORDER BY raw_body->>'demo') FROM messaging.webhook_envelope"));
    }

    [Theory]
    [InlineData("inbox_message", "processing_status", "Pending")]
    [InlineData("inbox_message", "processing_status", "Claimed")]
    [InlineData("inbox_message", "processing_status", "Failed")]
    [InlineData("outbox_message", "delivery_status", "Pending")]
    [InlineData("outbox_message", "delivery_status", "Claimed")]
    [InlineData("outbox_message", "delivery_status", "Failed")]
    public async Task D5_messaging_reset_refuses_and_deletes_nothing_while_work_is_open(
        string table,
        string statusColumn,
        string openStatus)
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var database = new DatabaseCatalogReader(connectionString);
        await using var provider = DemoDataHost.Build(connectionString, includeDemoData: true);

        await SeedMessagingAsync(provider);
        await database.ExecuteAsync(
            "UPDATE messaging.inbox_message SET processing_status = 'Processed', processed_at = now();"
            + "UPDATE messaging.outbox_message SET delivery_status = 'Sent', sent_at = now(), "
            + "provider_message_id = 'wamid.out-' || id;"
            + $"UPDATE messaging.{table} SET {statusColumn} = '{openStatus}' "
            + $"WHERE id = (SELECT min(id) FROM messaging.{table} WHERE customer_external_id = '{CustomerX}');");

        Assert.Equal(new DemoMessagingCounts(2, 1, 1), await CountMessagingAsync(provider, CustomerX));

        var result = await InScopeAsync(provider, (IDemoMessagingData data) => data.DeleteAsync([CustomerX]));

        Assert.Equal(new DemoMessagingDeleteResult(Refused: true, Inbox: 0, Outbox: 0, Envelopes: 0), result);
        Assert.Equal(new DemoMessagingCounts(2, 1, 1), await CountMessagingAsync(provider, CustomerX));
        Assert.Equal(new DemoMessagingCounts(2, 1, 0), await CountMessagingAsync(provider, CustomerY));
        Assert.Equal("3", await database.ScalarAsync("SELECT count(*) FROM messaging.webhook_envelope"));
    }

    [Fact]
    public async Task D6_application_composition_alone_resolves_no_demo_data_contract()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        await using (var production = DemoDataHost.Build(connectionString, includeDemoData: false))
        await using (var scope = production.CreateAsyncScope())
        {
            Assert.Null(scope.ServiceProvider.GetService<IDemoCatalogData>());
            Assert.Null(scope.ServiceProvider.GetService<IDemoBusinessInfoData>());
            Assert.Null(scope.ServiceProvider.GetService<IDemoConversationData>());
            Assert.Null(scope.ServiceProvider.GetService<IDemoMessagingData>());
        }

        await using (var demo = DemoDataHost.Build(connectionString, includeDemoData: true))
        await using (var scope = demo.CreateAsyncScope())
        {
            Assert.NotNull(scope.ServiceProvider.GetService<IDemoCatalogData>());
            Assert.NotNull(scope.ServiceProvider.GetService<IDemoBusinessInfoData>());
            Assert.NotNull(scope.ServiceProvider.GetService<IDemoConversationData>());
            Assert.NotNull(scope.ServiceProvider.GetService<IDemoMessagingData>());
        }
    }

    [Fact]
    public async Task D6_real_program_host_resolves_no_demo_data_contract()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        await using var factory = new ApplicationHostFactory(
            connectionString,
            StubOllamaTagsHandler.WithNames("qwen3.5:2b-q4_K_M"));
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Null(scope.ServiceProvider.GetService<IDemoCatalogData>());
        Assert.Null(scope.ServiceProvider.GetService<IDemoBusinessInfoData>());
        Assert.Null(scope.ServiceProvider.GetService<IDemoConversationData>());
        Assert.Null(scope.ServiceProvider.GetService<IDemoMessagingData>());
    }

    /// <summary>
    /// X owns one message in its own envelope and one in an envelope it shares with Y; Y also owns a
    /// message in its own envelope. Each customer has one Outbox row.
    /// </summary>
    private static async Task SeedMessagingAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageQueue>();
        var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>();

        await inbound.EnqueueAsync(Inbound("{\"demo\":\"x-only\"}", "wamid.x1", CustomerX));
        await inbound.EnqueueAsync(Inbound("{\"demo\":\"shared\"}", "wamid.x2", CustomerX));
        await inbound.EnqueueAsync(Inbound("{\"demo\":\"shared\"}", "wamid.y1", CustomerY));
        await inbound.EnqueueAsync(Inbound("{\"demo\":\"y-only\"}", "wamid.y2", CustomerY));

        await outbound.EnqueueAsync(new OutboundMessageRequest(1, CustomerX, "corr-x", "AI", "reply to x"));
        await outbound.EnqueueAsync(new OutboundMessageRequest(2, CustomerY, "corr-y", "AI", "reply to y"));
    }

    private static InboundMessageEnvelope Inbound(string rawBody, string providerMessageId, string customer) =>
        new(
            RawBody: rawBody,
            ProviderMessageId: providerMessageId,
            CustomerExternalId: customer,
            MessageType: "text",
            ProviderTimestamp: new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
            Body: "hello");

    private static Task<DemoMessagingCounts> CountMessagingAsync(ServiceProvider provider, string customer) =>
        InScopeAsync(provider, (IDemoMessagingData data) => data.CountAsync([customer]));

    private static Task<BusinessInfoValue?> ReadBusinessInfoAsync(ServiceProvider provider, string key) =>
        InScopeAsync(provider, (IStorefrontBusinessInfo info) => info.GetByKeyAsync(key));

    private static async Task<TResult> InScopeAsync<TService, TResult>(
        ServiceProvider provider,
        Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = provider.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static List<KeyValuePair<string, long>> Ordered(IReadOnlyDictionary<string, long> map) =>
        [.. map.OrderBy(pair => pair.Key, StringComparer.Ordinal)];

    private static List<BusinessInfoUpdate> AllBusinessInfo() =>
        [.. BusinessInfoKeyNames.All.Select(key => new BusinessInfoUpdate(key, $"seed-{key}", null, true))];

    private static List<DemoModelSeed> CatalogSeed(bool withSecondT2Variant = false) =>
    [
        new(
            ModelCode: "DEMO-T1",
            Brand: "Dell",
            Model: "T1 24",
            DisplayName: "Demo T1 24",
            SizeInches: 23.8m,
            PanelType: "IPS",
            ResolutionWidth: 1920,
            ResolutionHeight: 1080,
            RefreshRate: 60,
            Description: null,
            SearchTags: ["Office", "Programming"],
            Ports: [new("HDMI", 2), new("VGA", 1)],
            Variants:
            [
                Variant("DEMO-T1-A", "A", 2400m, quantity: 3, warrantyDays: 90),
                Variant("DEMO-T1-B", "B", 2100m),
            ]),
        new(
            ModelCode: "DEMO-T2",
            Brand: "HP",
            Model: "T2 27",
            DisplayName: "Demo T2 27",
            SizeInches: 27.0m,
            PanelType: "VA",
            ResolutionWidth: 2560,
            ResolutionHeight: 1440,
            RefreshRate: 75,
            Description: null,
            SearchTags: ["Design"],
            Ports: [new("HDMI", 1), new("DisplayPort", 1), new("USB-C", 1)],
            Variants: withSecondT2Variant
                ? [Variant("DEMO-T2-A", "A", 4100m), Variant("DEMO-T2-B", "B", 3600m)]
                : [Variant("DEMO-T2-A", "A", 4100m)]),
    ];

    private static DemoModelSeed Model(string modelCode, params DemoVariantSeed[] variants) =>
        new(
            ModelCode: modelCode,
            Brand: "Dell",
            Model: modelCode,
            DisplayName: modelCode,
            SizeInches: 24m,
            PanelType: "IPS",
            ResolutionWidth: 1920,
            ResolutionHeight: 1080,
            RefreshRate: 60,
            Description: null,
            SearchTags: [],
            Ports: [new("HDMI", 1)],
            Variants: variants);

    private static DemoVariantSeed Variant(string sku, string grade, decimal price, int quantity = 1, int warrantyDays = 30) =>
        new(sku, grade, price, quantity, warrantyDays, WarrantyNotes: null, CosmeticNotes: null);
}

/// <summary>
/// The real composition root over one test database, optionally with the demo-data composition the
/// operator tooling adds on top of it.
/// </summary>
internal static class DemoDataHost
{
    public static ServiceProvider Build(string connectionString, bool includeDemoData)
    {
        var configuration = new ConfigurationManager
        {
            [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
            ["Catalog:Search:SizeToleranceInches"] = "0.5",
            ["Catalog:Search:SoftBudgetTolerance"] = "0.10",
            [$"{WhatsAppOptions.ConfigurationSectionName}:ApiVersion"] = "v23.0",
            [$"{WhatsAppOptions.ConfigurationSectionName}:PhoneNumberId"] = "123456789",
            [$"{WhatsAppOptions.ConfigurationSectionName}:WabaId"] = "987654321",
            [$"{WhatsAppOptions.ConfigurationSectionName}:VerifyToken"] = "test-verify-token",
            [$"{WhatsAppOptions.ConfigurationSectionName}:AppSecret"] = "test-app-secret",
            [$"{WhatsAppOptions.ConfigurationSectionName}:AccessToken"] = "test-access-token",
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(configuration);

        if (includeDemoData)
        {
            services.AddDemoDataOperations();
        }

        return services.BuildServiceProvider();
    }
}
