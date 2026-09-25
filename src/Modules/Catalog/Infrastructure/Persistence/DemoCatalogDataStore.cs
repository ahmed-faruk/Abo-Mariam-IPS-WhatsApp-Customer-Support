using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The insert-only controlled-demo catalogue seed. Existing rows are only read, never updated, so no
/// audit row is needed: nothing an admin could have changed is touched. A SKU that already belongs to
/// a different model is rejected instead of being silently mapped to the wrong model.
/// </summary>
internal sealed class DemoCatalogDataStore(CatalogDbContext dbContext) : IDemoCatalogData
{
    public async Task<IReadOnlyDictionary<string, long>> EnsureSeedAsync(
        IReadOnlyList<DemoModelSeed> models,
        CancellationToken cancellationToken = default)
    {
        var skus = RequireValidSeed(models);
        var modelCodes = models.Select(model => model.ModelCode).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingModelIds = await dbContext.ProductModels
            .AsNoTracking()
            .Where(model => modelCodes.Contains(model.ModelCode))
            .ToDictionaryAsync(model => model.ModelCode, model => model.Id, StringComparer.Ordinal, cancellationToken);

        var existingVariants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => skus.Contains(variant.Sku))
            .Select(variant => new { variant.Sku, variant.ProductModel!.ModelCode })
            .ToDictionaryAsync(variant => variant.Sku, variant => variant.ModelCode, StringComparer.Ordinal, cancellationToken);

        // Every stored SKU is checked before any row is added, so a rejected seed leaves nothing pending.
        foreach (var seed in models)
        {
            foreach (var variant in seed.Variants)
            {
                if (existingVariants.TryGetValue(variant.Sku, out var storedModelCode)
                    && !string.Equals(storedModelCode, seed.ModelCode, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The SKU '{variant.Sku}' is stored under model '{storedModelCode}', "
                        + $"not under the seeded model '{seed.ModelCode}'.");
                }
            }
        }

        foreach (var seed in models)
        {
            var missingVariants = seed.Variants
                .Where(variant => !existingVariants.ContainsKey(variant.Sku))
                .Select(NewVariant)
                .ToList();

            if (existingModelIds.TryGetValue(seed.ModelCode, out var modelId))
            {
                foreach (var variant in missingVariants)
                {
                    variant.ProductModelId = modelId;
                    dbContext.ProductVariants.Add(variant);
                }

                continue;
            }

            dbContext.ProductModels.Add(NewModel(seed, missingVariants));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var seeded = await FindAsync(skus, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return seeded;
    }

    public async Task<IReadOnlyDictionary<string, long>> FindVariantIdsAsync(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skus);

        return await FindAsync([.. skus], cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, long>> FindAsync(
        List<string> skus,
        CancellationToken cancellationToken) =>
        await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => skus.Contains(variant.Sku))
            .ToDictionaryAsync(variant => variant.Sku, variant => variant.Id, StringComparer.Ordinal, cancellationToken);

    /// <summary>
    /// Rejects a seed that cannot be applied deterministically before anything is written, and returns
    /// every seeded SKU.
    /// </summary>
    private static List<string> RequireValidSeed(IReadOnlyList<DemoModelSeed> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        var modelCodes = new HashSet<string>(StringComparer.Ordinal);
        var skus = new List<string>();
        var seenSkus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            ArgumentNullException.ThrowIfNull(model, nameof(models));
            ArgumentException.ThrowIfNullOrWhiteSpace(model.ModelCode, nameof(models));
            ArgumentNullException.ThrowIfNull(model.SearchTags, nameof(models));
            ArgumentNullException.ThrowIfNull(model.Ports, nameof(models));
            ArgumentNullException.ThrowIfNull(model.Variants, nameof(models));

            if (!modelCodes.Add(model.ModelCode))
            {
                throw new ArgumentException($"The model code '{model.ModelCode}' is seeded twice.", nameof(models));
            }

            foreach (var variant in model.Variants)
            {
                ArgumentNullException.ThrowIfNull(variant, nameof(models));
                ArgumentException.ThrowIfNullOrWhiteSpace(variant.Sku, nameof(models));

                if (!seenSkus.Add(variant.Sku))
                {
                    throw new ArgumentException($"The SKU '{variant.Sku}' is seeded twice.", nameof(models));
                }

                skus.Add(variant.Sku);
            }
        }

        return skus;
    }

    private static ProductModel NewModel(DemoModelSeed seed, List<ProductVariant> variants) => new()
    {
        ModelCode = seed.ModelCode,
        Brand = seed.Brand,
        Model = seed.Model,
        DisplayName = seed.DisplayName,
        SizeInches = seed.SizeInches,
        PanelType = seed.PanelType,
        ResolutionWidth = seed.ResolutionWidth,
        ResolutionHeight = seed.ResolutionHeight,
        RefreshRate = seed.RefreshRate,
        Description = seed.Description,
        SearchTags = [.. seed.SearchTags],
        IsActive = true,
        Ports = [.. seed.Ports.Select(port => new ProductModelPort { PortType = port.PortType, Count = port.Count })],
        Variants = variants,
    };

    private static ProductVariant NewVariant(DemoVariantSeed seed) => new()
    {
        Sku = seed.Sku,
        Grade = seed.Grade,
        SellingPrice = seed.SellingPrice,
        Quantity = seed.Quantity,
        WarrantyDays = seed.WarrantyDays,
        WarrantyNotes = seed.WarrantyNotes,
        CosmeticNotes = seed.CosmeticNotes,
        IsActive = true,
    };
}
