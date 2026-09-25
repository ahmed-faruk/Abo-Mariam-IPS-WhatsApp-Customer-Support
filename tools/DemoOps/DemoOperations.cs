using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

namespace WhatsAppMonitorAssistant.Tools.DemoOps;

/// <summary>
/// The seed, reset and verify commands. Every write goes through a module contract: the insert-only
/// demo seeds, the named-customer demo resets, and the audited commercial and business-info updates
/// that restore the frozen values.
/// </summary>
internal sealed class DemoOperations(IServiceProvider services, TextWriter output)
{
    /// <summary>The actor recorded in the catalogue audit rows a reset writes.</summary>
    public const string ResetActor = "demo-reset";

    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        var (skus, insertedKeys) = await SeedCoreAsync(scope.ServiceProvider, cancellationToken);

        output.WriteLine(Invariant($"SEED OK: catalogue SKUs present={skus.Count}, business-info rows inserted={insertedKeys}"));

        return DemoCli.Success;
    }

    public async Task<int> ResetAsync(IReadOnlyList<string> customers, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var messaging = await provider.GetRequiredService<IDemoMessagingData>().DeleteAsync(customers, cancellationToken);

        if (messaging.Refused)
        {
            output.WriteLine(
                "RESET REFUSED: the named customers still have Pending, Claimed or Failed Messaging work; "
                + "nothing was changed.");

            return DemoCli.ResetRefused;
        }

        output.WriteLine(Invariant(
            $"RESET messaging: inbox={messaging.Inbox} outbox={messaging.Outbox} envelopes={messaging.Envelopes}"));

        var deletedCustomers = await provider.GetRequiredService<IDemoConversationData>()
            .DeleteCustomersAsync(customers, cancellationToken);

        output.WriteLine(Invariant($"RESET conversations: customers={deletedCustomers}"));

        var (skus, _) = await SeedCoreAsync(provider, cancellationToken);
        var commercial = provider.GetRequiredService<ICatalogCommercialUpdates>();
        var changedVariantValues = 0;

        foreach (var variant in DemoDataset.Catalogue.SelectMany(model => model.Variants))
        {
            var id = skus[variant.Sku];

            changedVariantValues += Changed(variant.Sku, await commercial.UpdatePriceAsync(
                new VariantPriceUpdate(id, variant.SellingPrice, ResetActor), cancellationToken));
            changedVariantValues += Changed(variant.Sku, await commercial.UpdateQuantityAsync(
                new VariantQuantityUpdate(id, variant.Quantity, ResetActor), cancellationToken));
            changedVariantValues += Changed(variant.Sku, await commercial.UpdateActiveStateAsync(
                new VariantActiveStateUpdate(id, true, ResetActor), cancellationToken));
        }

        output.WriteLine(Invariant($"RESET catalogue: variants={skus.Count} changed values={changedVariantValues}"));

        var businessInfo = provider.GetRequiredService<IStorefrontBusinessInfoUpdates>();
        var changedKeys = 0;

        foreach (var row in DemoDataset.BusinessInfo)
        {
            var outcome = await businessInfo.UpdateAsync(row, cancellationToken);

            changedKeys += outcome switch
            {
                BusinessInfoUpdateOutcome.Updated => 1,
                BusinessInfoUpdateOutcome.Unchanged => 0,
                _ => throw new InvalidOperationException($"The business info key '{row.Key}' could not be restored: {outcome}."),
            };
        }

        output.WriteLine(Invariant($"RESET business-info: keys={DemoDataset.BusinessInfo.Count} changed={changedKeys}"));
        output.WriteLine("RESET OK");

        return DemoCli.Success;
    }

    public async Task<int> VerifyAsync(IReadOnlyList<string> customers, CancellationToken cancellationToken)
    {
        var failures = 0;

        failures += await CheckAsync("tolerances", provider => Task.FromResult(CheckTolerances(provider)), cancellationToken);
        failures += await CheckAsync("catalogue", CheckCatalogueAsync, cancellationToken);
        failures += await CheckAsync("hard-budget-search", CheckHardBudgetSearchAsync, cancellationToken);
        failures += await CheckAsync("soft-budget-search", CheckSoftBudgetSearchAsync, cancellationToken);
        failures += await CheckAsync("business-info", CheckBusinessInfoAsync, cancellationToken);

        foreach (var customer in customers)
        {
            failures += await CheckAsync(
                $"customer {customer}",
                provider => CheckCustomerAsync(provider, customer, cancellationToken),
                cancellationToken);
        }

        WriteBaseline();

        output.WriteLine(failures == 0 ? "VERIFY PASS" : Invariant($"VERIFY FAIL: {failures} check(s) failed"));

        return failures == 0 ? DemoCli.Success : DemoCli.VerifyFailed;
    }

    private static async Task<(IReadOnlyDictionary<string, long> Skus, int InsertedKeys)> SeedCoreAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var skus = await provider.GetRequiredService<IDemoCatalogData>()
            .EnsureSeedAsync(DemoDataset.Catalogue, cancellationToken);
        var insertedKeys = await provider.GetRequiredService<IDemoBusinessInfoData>()
            .EnsureSeedAsync(DemoDataset.BusinessInfo, cancellationToken);

        return (skus, insertedKeys);
    }

    private static int Changed(string sku, CommercialUpdateOutcome outcome) => outcome switch
    {
        CommercialUpdateOutcome.Updated => 1,
        CommercialUpdateOutcome.Unchanged => 0,
        _ => throw new InvalidOperationException($"The variant '{sku}' could not be restored: {outcome}."),
    };

    /// <summary>
    /// Runs one check in its own scope and prints PASS or one FAIL line per mismatch. A check that
    /// throws is a failed check, so verification always reports every check.
    /// </summary>
    private async Task<int> CheckAsync(
        string name,
        Func<IServiceProvider, Task<List<string>>> check,
        CancellationToken cancellationToken)
    {
        List<string> problems;

        try
        {
            await using var scope = services.CreateAsyncScope();
            problems = await check(scope.ServiceProvider);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            problems = [$"{exception.GetType().Name}: {exception.Message}"];
        }

        if (problems.Count == 0)
        {
            output.WriteLine($"PASS {name}");

            return 0;
        }

        foreach (var problem in problems)
        {
            output.WriteLine($"FAIL {name}: {problem}");
        }

        return 1;
    }

    private static List<string> CheckTolerances(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<CatalogSearchOptions>();
        var problems = new List<string>();

        if (options.SizeToleranceInches != DemoDataset.DemoSizeToleranceInches)
        {
            problems.Add(Invariant(
                $"SizeToleranceInches={options.SizeToleranceInches} (expected {DemoDataset.DemoSizeToleranceInches})"));
        }

        if (options.SoftBudgetTolerance != DemoDataset.DemoSoftBudgetTolerance)
        {
            problems.Add(Invariant(
                $"SoftBudgetTolerance={options.SoftBudgetTolerance} (expected {DemoDataset.DemoSoftBudgetTolerance})"));
        }

        return problems;
    }

    private async Task<List<string>> CheckCatalogueAsync(IServiceProvider provider)
    {
        var problems = new List<string>();
        var allSkus = DemoDataset.Catalogue.SelectMany(model => model.Variants).Select(variant => variant.Sku).ToList();
        var ids = await provider.GetRequiredService<IDemoCatalogData>().FindVariantIdsAsync(allSkus);
        var details = provider.GetRequiredService<ICatalogProductDetails>();

        foreach (var seed in DemoDataset.Catalogue)
        {
            var anchor = seed.Variants.Select(variant => variant.Sku).FirstOrDefault(ids.ContainsKey);

            if (anchor is null)
            {
                problems.Add($"{seed.ModelCode} is missing");

                continue;
            }

            var facts = await details.GetVariantFactsAsync(ids[anchor]);
            var stored = facts is null ? null : await details.GetDetailsAsync(facts.ModelId);

            if (stored is null)
            {
                problems.Add($"{seed.ModelCode} is missing");

                continue;
            }

            CompareModel(seed, stored, problems);
        }

        output.WriteLine(Invariant(
            $"INFO catalogue: {DemoDataset.Catalogue.Count} models, {allSkus.Count} variants expected, {ids.Count} SKUs found"));

        return problems;
    }

    private static void CompareModel(DemoModelSeed seed, ProductDetails stored, List<string> problems)
    {
        var code = seed.ModelCode;

        Expect(problems, code, "model_code", stored.ModelCode, seed.ModelCode);
        Expect(problems, code, "brand", stored.Brand, seed.Brand);
        Expect(problems, code, "model", stored.Model, seed.Model);
        Expect(problems, code, "display_name", stored.DisplayName, seed.DisplayName);
        Expect(problems, code, "size_inches", stored.SizeInches, seed.SizeInches);
        Expect(problems, code, "panel_type", stored.PanelType, seed.PanelType);
        Expect(problems, code, "resolution", $"{stored.ResolutionWidth}x{stored.ResolutionHeight}", $"{seed.ResolutionWidth}x{seed.ResolutionHeight}");
        Expect(problems, code, "refresh_rate", stored.RefreshRate, seed.RefreshRate);
        Expect(problems, code, "description", stored.Description, seed.Description);
        Expect(problems, code, "tags", string.Join(",", stored.Tags), string.Join(",", seed.SearchTags));
        Expect(problems, code, "ports", PortList(stored.Ports.Select(port => (port.PortType, port.Count))), PortList(seed.Ports.Select(port => (port.PortType, port.Count))));
        Expect(problems, code, "model_active", stored.IsActive, true);
        Expect(
            problems,
            code,
            "skus",
            string.Join(",", stored.Variants.Select(variant => variant.Sku).Order(StringComparer.Ordinal)),
            string.Join(",", seed.Variants.Select(variant => variant.Sku).Order(StringComparer.Ordinal)));

        foreach (var variant in seed.Variants)
        {
            var storedVariant = stored.Variants.FirstOrDefault(candidate => candidate.Sku == variant.Sku);

            if (storedVariant is null)
            {
                continue;
            }

            var sku = variant.Sku;

            Expect(problems, sku, "grade", storedVariant.Grade, variant.Grade);
            Expect(problems, sku, "price", Price(storedVariant.Price), Price(variant.SellingPrice));
            Expect(problems, sku, "quantity", storedVariant.Quantity, variant.Quantity);
            Expect(problems, sku, "warranty_days", storedVariant.WarrantyDays, variant.WarrantyDays);
            Expect(problems, sku, "warranty_notes", storedVariant.WarrantyNotes, variant.WarrantyNotes);
            Expect(problems, sku, "cosmetic_notes", storedVariant.CosmeticNotes, variant.CosmeticNotes);
            Expect(problems, sku, "active", storedVariant.IsActive, true);
            Expect(problems, sku, "available", storedVariant.IsAvailable, variant.Quantity > 0);
        }
    }

    private async Task<List<string>> CheckHardBudgetSearchAsync(IServiceProvider provider)
    {
        var results = await provider.GetRequiredService<ICatalogSearch>().SearchAsync(DemoDataset.HardBudgetQuery);
        var problems = new List<string>();

        output.WriteLine($"INFO hard-budget-search results: {Describe(results)}");

        if (results.Count == 0 || results[0].ModelCode != DemoDataset.KeyModelCode)
        {
            problems.Add(
                $"first result is {(results.Count == 0 ? "<none>" : results[0].ModelCode)} (expected {DemoDataset.KeyModelCode})");
        }

        foreach (var result in results.Where(result => result.Price > DemoDataset.HardBudgetCeiling))
        {
            problems.Add(Invariant($"{result.Sku} price={Price(result.Price)} exceeds {Price(DemoDataset.HardBudgetCeiling)}"));
        }

        return problems;
    }

    private async Task<List<string>> CheckSoftBudgetSearchAsync(IServiceProvider provider)
    {
        var results = await provider.GetRequiredService<ICatalogSearch>().SearchAsync(DemoDataset.SoftBudgetQuery);
        var problems = new List<string>();

        output.WriteLine($"INFO soft-budget-search results: {Describe(results)}");

        if (results.Count == 0)
        {
            problems.Add("no result");
        }

        foreach (var result in results.Where(result => result.Price > DemoDataset.SoftBudgetCeiling))
        {
            problems.Add(Invariant($"{result.Sku} price={Price(result.Price)} exceeds {Price(DemoDataset.SoftBudgetCeiling)}"));
        }

        return problems;
    }

    private static async Task<List<string>> CheckBusinessInfoAsync(IServiceProvider provider)
    {
        var reader = provider.GetRequiredService<IStorefrontBusinessInfo>();
        var problems = new List<string>();

        foreach (var row in DemoDataset.BusinessInfo)
        {
            var stored = await reader.GetByKeyAsync(row.Key);

            if (stored is null)
            {
                problems.Add($"{row.Key} is missing or inactive");
            }
            else if (stored.AnswerAr != row.AnswerAr || stored.AnswerEn != row.AnswerEn)
            {
                problems.Add($"{row.Key} differs from the approved value");
            }
        }

        return problems;
    }

    private static async Task<List<string>> CheckCustomerAsync(
        IServiceProvider provider,
        string customer,
        CancellationToken cancellationToken)
    {
        var conversations = await provider.GetRequiredService<IDemoConversationData>()
            .CountConversationsAsync([customer], cancellationToken);
        var messaging = await provider.GetRequiredService<IDemoMessagingData>()
            .CountAsync([customer], cancellationToken);

        return conversations == 0 && messaging.Inbox == 0 && messaging.Outbox == 0
            ? []
            : [Invariant($"conversations={conversations} inbox={messaging.Inbox} outbox={messaging.Outbox} (expected 0 each)")];
    }

    /// <summary>The frozen values a successful verification certifies, for the operator's evidence.</summary>
    private void WriteBaseline()
    {
        output.WriteLine(Invariant($"BASELINE tolerance Catalog:Search:SizeToleranceInches={DemoDataset.DemoSizeToleranceInches}"));
        output.WriteLine(Invariant($"BASELINE tolerance Catalog:Search:SoftBudgetTolerance={DemoDataset.DemoSoftBudgetTolerance}"));

        foreach (var model in DemoDataset.Catalogue)
        {
            foreach (var variant in model.Variants)
            {
                output.WriteLine(string.Join(
                    " | ",
                    $"BASELINE catalogue {variant.Sku}",
                    model.DisplayName,
                    Invariant($"{model.SizeInches} in"),
                    model.PanelType,
                    Invariant($"{model.ResolutionWidth}x{model.ResolutionHeight} {model.RefreshRate}Hz"),
                    PortList(model.Ports.Select(port => (port.PortType, port.Count))),
                    $"grade {variant.Grade}",
                    $"price {Price(variant.SellingPrice)}",
                    Invariant($"quantity {variant.Quantity}"),
                    Invariant($"warranty {variant.WarrantyDays} days")));
            }
        }

        foreach (var row in DemoDataset.BusinessInfo)
        {
            output.WriteLine($"BASELINE business-info {row.Key} | {row.AnswerAr}");
        }
    }

    private static void Expect<T>(List<string> problems, string subject, string field, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            problems.Add(Invariant($"{subject} {field}={Text(actual)} (expected {Text(expected)})"));
        }
    }

    private static string Text<T>(T value) => value switch
    {
        null => "<null>",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Describe(IReadOnlyList<ProductRecommendation> results) =>
        results.Count == 0
            ? "<none>"
            : string.Join(", ", results.Select(result => Invariant($"{result.Sku} {Price(result.Price)}")));

    private static string PortList(IEnumerable<(string PortType, int Count)> ports) =>
        string.Join(
            ",",
            ports
                .Select(port => Invariant($"{port.PortType}:{port.Count}"))
                .Order(StringComparer.Ordinal));

    private static string Price(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
