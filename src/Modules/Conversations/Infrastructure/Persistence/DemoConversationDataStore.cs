using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// The controlled-demo conversation reset. Deleting the customer row is the whole operation: the
/// conversation and conversation-state foreign keys cascade, so no conversation of a removed customer
/// and no carried reference, shortlist or Human mode can survive it.
/// </summary>
internal sealed class DemoConversationDataStore(ConversationDbContext dbContext) : IDemoConversationData
{
    private const string CountConversationsSql = """
        SELECT count(*)::int
        FROM conversations.conversation c
        JOIN conversations.customer cu ON cu.id = c.customer_id
        WHERE cu.whatsapp_number = ANY(@customer_ids);
        """;

    private const string DeleteCustomersSql = """
        DELETE FROM conversations.customer
        WHERE whatsapp_number = ANY(@customer_ids);
        """;

    public async Task<int> CountConversationsAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default)
    {
        var ids = RequireCustomerIds(customerExternalIds);

        await using var command = await CreateAsync(CountConversationsSql, ids, cancellationToken);

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<int> DeleteCustomersAsync(
        IReadOnlyList<string> customerExternalIds,
        CancellationToken cancellationToken = default)
    {
        var ids = RequireCustomerIds(customerExternalIds);

        await using var command = await CreateAsync(DeleteCustomersSql, ids, cancellationToken);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DbCommand> CreateAsync(string sql, string[] ids, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "customer_ids";
        parameter.Value = ids;
        command.Parameters.Add(parameter);

        return command;
    }

    /// <summary>At least one customer id and no blank one, so a reset can never target everybody.</summary>
    private static string[] RequireCustomerIds(IReadOnlyList<string> customerExternalIds)
    {
        ArgumentNullException.ThrowIfNull(customerExternalIds);

        if (customerExternalIds.Count == 0)
        {
            throw new ArgumentException("At least one customer id is required.", nameof(customerExternalIds));
        }

        foreach (var id in customerExternalIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(customerExternalIds));
        }

        return [.. customerExternalIds.Distinct(StringComparer.Ordinal)];
    }
}
