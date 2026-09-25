using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The read-only Admin Lite grid. The order is byte order of the lowercased model code, so it does not
/// depend on the database collation, then grade, then variant id.
/// </summary>
internal sealed class CatalogAdminGridReader(CatalogDbContext dbContext) : ICatalogAdminGrid
{
    private const int MaxRows = 500;

    public async Task<IReadOnlyList<CatalogGridRow>> ListVariantsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ProductVariants
            .AsNoTracking()
            .OrderBy(variant => EF.Functions.Collate(variant.ProductModel!.ModelCode.ToLower(), "C"))
            .ThenBy(variant => variant.Grade)
            .ThenBy(variant => variant.Id)
            .Take(MaxRows)
            .Select(variant => new CatalogGridRow(
                variant.ProductModelId,
                variant.ProductModel!.ModelCode,
                variant.ProductModel.DisplayName,
                variant.Id,
                variant.Sku,
                variant.Grade,
                variant.SellingPrice,
                variant.Quantity,
                variant.IsActive))
            .ToListAsync(cancellationToken);
}
