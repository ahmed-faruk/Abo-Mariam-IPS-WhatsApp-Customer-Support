using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Features.GetBusinessInfo;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>
/// The authoritative read side of the business information. The query is a plain EF Core query, so
/// Npgsql translates it and every answer comes from the current stored row.
/// </summary>
internal sealed class BusinessInfoReader(StorefrontDbContext dbContext) : IBusinessInfoReader
{
    public async Task<BusinessInfoValue?> GetActiveByKeyAsync(
        string canonicalKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.BusinessInfo
            .AsNoTracking()
            .Where(info => info.Key == canonicalKey && info.IsActive)
            .Select(info => new BusinessInfoValue(
                info.Key,
                info.AnswerAr,
                info.AnswerEn,
                info.IsActive,
                info.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
