using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Features.UpdateBusinessInfo;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>
/// The admin business-info update. Each change is one PostgreSQL transaction that locks the stored
/// row, compares the stored values with the requested ones and then writes them, so a concurrent
/// update of the same key cannot slip between the comparison and the write.
/// </summary>
internal sealed class BusinessInfoUpdateStore(StorefrontDbContext dbContext) : IBusinessInfoUpdateStore
{
    /// <summary>
    /// Reads the current values of the row and holds it until the transaction ends, so the comparison
    /// below sees exactly the state the write replaces. A missing row is the NotFound result, because
    /// an update never creates business information.
    /// </summary>
    private const string LockSql = """
        SELECT answer_ar, answer_en, is_active
        FROM storefront.business_info
        WHERE "key" = @key
        FOR UPDATE;
        """;

    /// <summary>
    /// Writes the change. <c>updated_at</c> is set by PostgreSQL itself, so the row's own clock stays
    /// the authority for when the value last changed. It is <c>clock_timestamp()</c> rather than
    /// <c>now()</c>, because <c>now()</c> is pinned to the transaction start: an update that waited on
    /// the row lock would otherwise record the earlier begin time instead of the time it wrote.
    /// </summary>
    private const string UpdateSql = """
        UPDATE storefront.business_info
        SET answer_ar = @answer_ar,
            answer_en = @answer_en,
            is_active = @is_active,
            updated_at = clock_timestamp()
        WHERE "key" = @key;
        """;

    public async Task<BusinessInfoUpdateOutcome> UpdateAsync(
        BusinessInfoChange change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var dbTransaction = transaction.GetDbTransaction();
        var current = await LockCurrentAsync(connection, dbTransaction, change.Key, cancellationToken);

        if (current is null)
        {
            return BusinessInfoUpdateOutcome.NotFound;
        }

        // A request that changes nothing is not written at all, so it neither moves updated_at nor
        // reports an update that did not happen.
        if (current.AnswerAr == change.AnswerAr
            && current.AnswerEn == change.AnswerEn
            && current.IsActive == change.IsActive)
        {
            return BusinessInfoUpdateOutcome.Unchanged;
        }

        await using (var command = CreateCommand(connection, dbTransaction, UpdateSql))
        {
            AddParameter(command, "key", change.Key);
            AddParameter(command, "answer_ar", change.AnswerAr);
            AddParameter(command, "answer_en", change.AnswerEn);
            AddParameter(command, "is_active", change.IsActive);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return BusinessInfoUpdateOutcome.Updated;
    }

    private static async Task<BusinessInfoState?> LockCurrentAsync(
        DbConnection connection,
        DbTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, LockSql);
        AddParameter(command, "key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new BusinessInfoState(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2))
            : null;
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>The locked stored values, captured before the change is applied.</summary>
    private sealed record BusinessInfoState(string AnswerAr, string? AnswerEn, bool IsActive);
}
