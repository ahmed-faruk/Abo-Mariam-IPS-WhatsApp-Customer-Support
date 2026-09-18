using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence;

/// <summary>
/// Small ADO.NET helper for the queue statements. The durable queue is deliberately expressed as
/// PostgreSQL (see docs/TECHNICAL.md section 15), not as a tracked EF Core read-modify-write.
/// </summary>
internal static class MessagingQueueCommands
{
    /// <summary>
    /// EF opens its connection lazily, so every raw queue statement opens it explicitly first.
    /// </summary>
    public static async Task<DbConnection> OpenAsync(MessagingDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    public static DbCommand Create(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    public static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Treats an unset <see cref="DateTime.Kind"/> as UTC so Npgsql can store timestamptz.</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Explains a completion or a failure that matched no row. A message that is still stored but
    /// not owned by the presented claim token belongs to somebody else now, because its lease
    /// expired and the partition moved on; only a message that is really gone is a missing row.
    /// </summary>
    public static async Task<Exception> LostClaimAsync(
        DbConnection connection,
        string statusSql,
        string messageName,
        long messageId,
        CancellationToken cancellationToken)
    {
        await using var command = Create(connection, null, statusSql);
        Add(command, "message_id", messageId);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string;

        return status is null
            ? new InvalidOperationException($"The {messageName} {messageId} does not exist.")
            : new ClaimOwnershipLostException(
                $"The {messageName} {messageId} is {status} and the presented claim token does not own "
                + "it, so its outcome was not recorded.");
    }
}
