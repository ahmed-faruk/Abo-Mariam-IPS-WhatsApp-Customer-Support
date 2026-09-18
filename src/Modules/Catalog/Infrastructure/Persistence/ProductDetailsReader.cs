using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.GetProductDetails;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The authoritative read side of the catalogue. Both queries are plain EF Core queries, so Npgsql
/// translates them and every value the reply renders comes from the current database row.
/// </summary>
internal sealed class ProductDetailsReader(CatalogDbContext dbContext) : IProductDetailsReader
{
    public async Task<ProductDetails?> GetDetailsAsync(long productModelId, CancellationToken cancellationToken)
    {
        var details = await dbContext.ProductModels
            .AsNoTracking()
            .Where(model => model.Id == productModelId)
            .Select(model => new ProductDetails
            {
                ModelId = model.Id,
                ModelCode = model.ModelCode,
                Brand = model.Brand,
                Model = model.Model,
                DisplayName = model.DisplayName,
                SizeInches = model.SizeInches,
                PanelType = model.PanelType,
                ResolutionWidth = model.ResolutionWidth,
                ResolutionHeight = model.ResolutionHeight,
                RefreshRate = model.RefreshRate,
                Description = model.Description,
                Tags = model.SearchTags,
                IsActive = model.IsActive,
                Ports = model.Ports
                    .OrderBy(port => port.PortType)
                    .Select(port => new ProductPortFact(port.PortType, port.Count))
                    .ToList(),
                Variants = model.Variants
                    .OrderBy(variant => variant.Grade)
                    .ThenBy(variant => variant.Id)
                    .Select(variant => new ProductVariantDetails
                    {
                        VariantId = variant.Id,
                        Sku = variant.Sku,
                        Grade = variant.Grade,
                        Price = variant.SellingPrice,
                        Quantity = variant.Quantity,
                        WarrantyDays = variant.WarrantyDays,
                        WarrantyNotes = variant.WarrantyNotes,
                        CosmeticNotes = variant.CosmeticNotes,
                        IsActive = variant.IsActive,
                    })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (details is null)
        {
            return null;
        }

        // Availability is the documented rule of docs/PLAN.md section 7, applied to the values the
        // query just read, so an inactive model marks every one of its variants unavailable.
        return details with
        {
            Variants =
            [
                .. details.Variants.Select(variant => variant with
                {
                    IsAvailable = AvailabilityRules.IsAvailable(details.IsActive, variant.IsActive, variant.Quantity),
                }),
            ],
        };
    }

    public async Task<ProductRecommendation?> GetVariantFactsAsync(
        long productVariantId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.Id == productVariantId)
            .Select(variant => new
            {
                ModelId = variant.ProductModel!.Id,
                variant.ProductModel!.ModelCode,
                variant.ProductModel!.Brand,
                variant.ProductModel!.Model,
                variant.ProductModel!.DisplayName,
                variant.ProductModel!.SizeInches,
                variant.ProductModel!.PanelType,
                variant.ProductModel!.ResolutionWidth,
                variant.ProductModel!.ResolutionHeight,
                variant.ProductModel!.RefreshRate,
                variant.ProductModel!.SearchTags,
                ModelIsActive = variant.ProductModel!.IsActive,
                Ports = variant.ProductModel!.Ports
                    .OrderBy(port => port.PortType)
                    .Select(port => port.PortType)
                    .ToList(),
                VariantId = variant.Id,
                variant.Sku,
                variant.Grade,
                Price = variant.SellingPrice,
                variant.Quantity,
                variant.WarrantyDays,
                variant.WarrantyNotes,
                variant.CosmeticNotes,
                variant.IsActive,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ProductRecommendation
            {
                ModelId = row.ModelId,
                ModelCode = row.ModelCode,
                Brand = row.Brand,
                Model = row.Model,
                DisplayName = row.DisplayName,
                SizeInches = row.SizeInches,
                PanelType = row.PanelType,
                ResolutionWidth = row.ResolutionWidth,
                ResolutionHeight = row.ResolutionHeight,
                RefreshRate = row.RefreshRate,
                Tags = row.SearchTags,
                Ports = row.Ports,
                VariantId = row.VariantId,
                Sku = row.Sku,
                Grade = row.Grade,
                Price = row.Price,
                Quantity = row.Quantity,
                WarrantyDays = row.WarrantyDays,
                WarrantyNotes = row.WarrantyNotes,
                CosmeticNotes = row.CosmeticNotes,
                IsAvailable = AvailabilityRules.IsAvailable(row.ModelIsActive, row.IsActive, row.Quantity),
            };
    }
}
