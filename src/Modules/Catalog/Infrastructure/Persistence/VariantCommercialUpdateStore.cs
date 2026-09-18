using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.UpdateVariantCommercials;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The admin commercial updates. Each change is one PostgreSQL transaction that locks the variant
/// row, captures the old value, writes the new value and inserts the audit row, so the catalogue can
/// never change without a matching audit entry and a failed audit rolls the change back with it.
/// </summary>
internal sealed class VariantCommercialUpdateStore(CatalogDbContext dbContext) : IVariantCommercialUpdateStore
{
    /// <summary>
    /// Reads the current state of the variant and holds the row until the transaction ends, so a
    /// concurrent update cannot slip between the captured old value and the write.
    /// </summary>
    private const string LockVariantSql = """
        SELECT selling_price, quantity, is_active
        FROM catalog.product_variant
        WHERE id = @variant_id
        FOR UPDATE;
        """;

    /// <summary>
    /// Locks the variant row and asks PostgreSQL to normalize the requested price to the column's own
    /// representation in the same statement, so the no-op check compares the value PostgreSQL would
    /// actually store instead of the raw request.
    /// </summary>
    private const string LockVariantPriceSql = """
        SELECT selling_price, (@requested_price::numeric(12,2)) AS requested_price
        FROM catalog.product_variant
        WHERE id = @variant_id
        FOR UPDATE;
        """;

    private const string UpdatePriceSql = """
        UPDATE catalog.product_variant
        SET selling_price = @selling_price::numeric(12,2),
            updated_at = now()
        WHERE id = @variant_id
        RETURNING selling_price;
        """;

    private const string UpdateQuantitySql = """
        UPDATE catalog.product_variant
        SET quantity = @quantity,
            updated_at = now()
        WHERE id = @variant_id
        RETURNING quantity;
        """;

    private const string UpdateActiveStateSql = """
        UPDATE catalog.product_variant
        SET is_active = @is_active,
            updated_at = now()
        WHERE id = @variant_id
        RETURNING is_active;
        """;

    public async Task<CommercialUpdateOutcome> UpdatePriceAsync(
        VariantPriceUpdate update,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await CatalogSqlCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var current = await LockVariantPriceAsync(connection, dbTransaction, update, cancellationToken);

        if (current is null)
        {
            return CommercialUpdateOutcome.VariantNotFound;
        }

        // Both sides of the comparison carry the column's numeric(12,2) representation, so a request
        // that only differs beyond the stored scale is a no-op: no update, no moved updated_at and no
        // audit row claiming a change that PostgreSQL would not store.
        if (current.Price == current.NormalizedPrice)
        {
            return CommercialUpdateOutcome.Unchanged;
        }

        // The statement returns what PostgreSQL stored, so the audit row records the persisted value
        // rather than the requested one.
        decimal storedPrice;

        await using (var command = CatalogSqlCommands.Create(connection, dbTransaction, UpdatePriceSql))
        {
            CatalogSqlCommands.Add(command, "variant_id", update.VariantId);
            CatalogSqlCommands.Add(command, "selling_price", current.NormalizedPrice);

            storedPrice = (decimal)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        return await AuditAndCommitAsync(
            transaction,
            CatalogAuditActions.UpdatePrice,
            update.VariantId,
            PriceJson(current.Price),
            PriceJson(storedPrice),
            update.ActorUserId,
            cancellationToken);
    }

    public async Task<CommercialUpdateOutcome> UpdateQuantityAsync(
        VariantQuantityUpdate update,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await CatalogSqlCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var current = await LockVariantAsync(connection, dbTransaction, update.VariantId, cancellationToken);

        if (current is null)
        {
            return CommercialUpdateOutcome.VariantNotFound;
        }

        if (current.Quantity == update.Quantity)
        {
            return CommercialUpdateOutcome.Unchanged;
        }

        int storedQuantity;

        await using (var command = CatalogSqlCommands.Create(connection, dbTransaction, UpdateQuantitySql))
        {
            CatalogSqlCommands.Add(command, "variant_id", update.VariantId);
            CatalogSqlCommands.Add(command, "quantity", update.Quantity);

            storedQuantity = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        return await AuditAndCommitAsync(
            transaction,
            CatalogAuditActions.UpdateQuantity,
            update.VariantId,
            QuantityJson(current.Quantity),
            QuantityJson(storedQuantity),
            update.ActorUserId,
            cancellationToken);
    }

    public async Task<CommercialUpdateOutcome> UpdateActiveStateAsync(
        VariantActiveStateUpdate update,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = await CatalogSqlCommands.OpenAsync(dbContext, cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var current = await LockVariantAsync(connection, dbTransaction, update.VariantId, cancellationToken);

        if (current is null)
        {
            return CommercialUpdateOutcome.VariantNotFound;
        }

        if (current.IsActive == update.IsActive)
        {
            return CommercialUpdateOutcome.Unchanged;
        }

        bool storedIsActive;

        await using (var command = CatalogSqlCommands.Create(connection, dbTransaction, UpdateActiveStateSql))
        {
            CatalogSqlCommands.Add(command, "variant_id", update.VariantId);
            CatalogSqlCommands.Add(command, "is_active", update.IsActive);

            storedIsActive = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        return await AuditAndCommitAsync(
            transaction,
            CatalogAuditActions.UpdateActiveState,
            update.VariantId,
            ActiveStateJson(current.IsActive),
            ActiveStateJson(storedIsActive),
            update.ActorUserId,
            cancellationToken);
    }

    /// <summary>
    /// Writes the audit row and commits. The insert shares the transaction of the change, so a
    /// failure here rolls the change back and the stored state and the audit rows stay consistent.
    /// </summary>
    private async Task<CommercialUpdateOutcome> AuditAndCommitAsync(
        IDbContextTransaction transaction,
        string action,
        long variantId,
        string oldJson,
        string newJson,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLog.Add(new CatalogAuditLog
        {
            EntityType = CatalogAuditEntityTypes.ProductVariant,
            EntityId = variantId,
            Action = action,
            OldJson = oldJson,
            NewJson = newJson,
            UserId = actorUserId,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CommercialUpdateOutcome.Updated;
    }

    private static async Task<VariantState?> LockVariantAsync(
        DbConnection connection,
        DbTransaction transaction,
        long variantId,
        CancellationToken cancellationToken)
    {
        await using var command = CatalogSqlCommands.Create(connection, transaction, LockVariantSql);
        CatalogSqlCommands.Add(command, "variant_id", variantId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new VariantState(reader.GetDecimal(0), reader.GetInt32(1), reader.GetBoolean(2))
            : null;
    }

    /// <summary>
    /// The locked current price and the requested price as PostgreSQL normalizes it to
    /// <c>numeric(12,2)</c>, which is the exact representation the update would store.
    /// </summary>
    private static async Task<VariantPriceState?> LockVariantPriceAsync(
        DbConnection connection,
        DbTransaction transaction,
        VariantPriceUpdate update,
        CancellationToken cancellationToken)
    {
        await using var command = CatalogSqlCommands.Create(connection, transaction, LockVariantPriceSql);
        CatalogSqlCommands.Add(command, "variant_id", update.VariantId);
        CatalogSqlCommands.Add(command, "requested_price", update.Price);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new VariantPriceState(reader.GetDecimal(0), reader.GetDecimal(1))
            : null;
    }

    private static string PriceJson(decimal price) => JsonSerializer.Serialize(new { selling_price = price });

    private static string QuantityJson(int quantity) => JsonSerializer.Serialize(new { quantity });

    private static string ActiveStateJson(bool isActive) => JsonSerializer.Serialize(new { is_active = isActive });

    /// <summary>The locked state of the variant, captured before the change is applied.</summary>
    private sealed record VariantState(decimal Price, int Quantity, bool IsActive);

    /// <summary>The locked price of the variant, with the requested price normalized by PostgreSQL.</summary>
    private sealed record VariantPriceState(decimal Price, decimal NormalizedPrice);
}
