using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>The read-only Admin Lite conversation list. PostgreSQL computes the last activity and the order.</summary>
internal sealed class ConversationAdminReader(ConversationDbContext dbContext) : IConversationAdminReads
{
    private const string SelectSql = """
        SELECT c.id, cu.whatsapp_number, c.mode, c.started_at,
               greatest(c.started_at, c.last_inbound_at, c.last_outbound_at) AS last_activity_at
        FROM conversations.conversation c
        JOIN conversations.customer cu ON cu.id = c.customer_id
        """;

    public async Task<IReadOnlyList<ConversationListRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await QueryAsync($"{SelectSql} ORDER BY last_activity_at DESC, c.id DESC LIMIT 200;", null, cancellationToken);

    public async Task<ConversationListRow?> GetAsync(long conversationId, CancellationToken cancellationToken = default) =>
        (await QueryAsync($"{SelectSql} WHERE c.id = @conversation_id;", conversationId, cancellationToken))
            .SingleOrDefault();

    private async Task<List<ConversationListRow>> QueryAsync(
        string sql,
        long? conversationId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (conversationId is { } id)
        {
            AddParameter(command, "conversation_id", id);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ConversationListRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ConversationListRow(
                reader.GetInt64(0),
                reader.GetString(1),
                ConversationModes.ToContract(reader.GetString(2)),
                reader.GetDateTime(3),
                reader.GetDateTime(4)));
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
