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

    /// <summary>
    /// Creates one durable queue command with an explicit finite command timeout. Without it the
    /// command would silently inherit the connection string's <c>Command Timeout</c>, which an
    /// operator can set to zero and thereby make a queue statement unbounded; the worker timing
    /// policy is the single owner of that bound, and the caller's cancellation token can still stop
    /// the command earlier.
    /// </summary>
    public static DbCommand Create(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        TimeSpan commandTimeout)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds(commandTimeout);

        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    /// <summary>A database timeout is expressed in whole seconds, so a sub-second policy is one second.</summary>
    private static int CommandTimeoutSeconds(TimeSpan commandTimeout) =>
        Math.Max(1, (int)Math.Ceiling(commandTimeout.TotalSeconds));

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
        TimeSpan commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var command = Create(connection, null, statusSql, commandTimeout);
        Add(command, "message_id", messageId);

        var status = await command.ExecuteScalarAsync(cancellationToken) as string;

        return status is null
            ? new InvalidOperationException($"The {messageName} {messageId} does not exist.")
            : new ClaimOwnershipLostException(
                $"The {messageName} {messageId} is {status} and the presented claim token does not own "
                + "it, so its outcome was not recorded.");
    }
}
