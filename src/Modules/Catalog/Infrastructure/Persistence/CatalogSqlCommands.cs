using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Small ADO.NET helper for the catalogue statements that are deliberately expressed as PostgreSQL,
/// such as the ranked search and the row lock of a commercial update.
/// </summary>
internal static class CatalogSqlCommands
{
    /// <summary>EF opens its connection lazily, so every raw statement opens it explicitly first.</summary>
    public static async Task<DbConnection> OpenAsync(CatalogDbContext dbContext, CancellationToken cancellationToken)
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
}
