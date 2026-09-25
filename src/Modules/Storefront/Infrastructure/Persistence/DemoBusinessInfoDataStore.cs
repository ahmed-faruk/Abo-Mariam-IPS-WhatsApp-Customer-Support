using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>
/// The insert-only controlled-demo business-info seed. Every row passes the same allowlist and answer
/// rules as an admin update before anything is written, and an approved key that already has a stored
/// row keeps it, so a reseed never overwrites an edited value.
/// </summary>
internal sealed class DemoBusinessInfoDataStore(StorefrontDbContext dbContext) : IDemoBusinessInfoData
{
    private const string InsertMissingSql = """
        INSERT INTO storefront.business_info ("key", answer_ar, answer_en, is_active)
        VALUES (@key, @answer_ar, @answer_en, @is_active)
        ON CONFLICT ("key") DO NOTHING;
        """;

    public async Task<int> EnsureSeedAsync(
        IReadOnlyList<BusinessInfoUpdate> rows,
        CancellationToken cancellationToken = default)
    {
        var validated = RequireValidRows(rows);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var inserted = 0;

        foreach (var row in validated)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = InsertMissingSql;
            command.Transaction = transaction.GetDbTransaction();
            AddParameter(command, "key", row.Key);
            AddParameter(command, "answer_ar", row.AnswerAr);
            AddParameter(command, "answer_en", row.AnswerEn);
            AddParameter(command, "is_active", row.IsActive);

            inserted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return inserted;
    }

    private static List<BusinessInfoUpdate> RequireValidRows(IReadOnlyList<BusinessInfoUpdate> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<BusinessInfoUpdate>(rows.Count);

        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(rows));

            var key = BusinessInfoKeys.RequireAllowed(row.Key);

            if (!keys.Add(key))
            {
                throw new ArgumentException($"The business info key '{key}' is seeded twice.", nameof(rows));
            }

            validated.Add(new BusinessInfoUpdate(
                key,
                BusinessInfoAnswers.RequireAnswerAr(row.AnswerAr),
                BusinessInfoAnswers.NormalizeAnswerEn(row.AnswerEn),
                row.IsActive));
        }

        return validated;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
