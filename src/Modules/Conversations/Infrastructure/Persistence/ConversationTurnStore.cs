using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// The Conversations persistence of one inbound turn. Customer and active-conversation creation use
/// PostgreSQL conflict handling instead of a read-then-write, so two concurrent turns of the same
/// customer converge on one customer row and one active conversation row and then share them.
/// </summary>
internal sealed class ConversationTurnStore(
    ConversationDbContext dbContext,
    ConversationOperationCoordinator coordinator) : IConversationTurnStore
{
    // The customer is identified by their WhatsApp number, which is unique, so a losing racer simply
    // keeps the row the winner inserted.
    private const string InsertCustomerSql = """
        INSERT INTO conversations.customer (whatsapp_number, first_seen_at, last_seen_at)
        VALUES (@whatsapp_number, @now, @now)
        ON CONFLICT (whatsapp_number) DO NOTHING
        RETURNING id;
        """;

    private const string SelectCustomerSql = """
        SELECT id FROM conversations.customer WHERE whatsapp_number = @whatsapp_number;
        """;

    // The active conversation is the one covered by ux_conversation_open_customer: a partial unique
    // index on the customer for every non-closed conversation. A closed conversation is history, so a
    // later inbound creates a brand-new AI conversation instead of reopening it.
    private const string InsertConversationSql = """
        INSERT INTO conversations.conversation (customer_id, mode, started_at, created_at, updated_at)
        VALUES (@customer_id, 'AI', @now, @now, @now)
        ON CONFLICT (customer_id) WHERE mode <> 'Closed' DO NOTHING
        RETURNING id;
        """;

    private const string SelectActiveConversationSql = """
        SELECT id FROM conversations.conversation
        WHERE customer_id = @customer_id AND mode <> 'Closed'
        ORDER BY id
        LIMIT 1;
        """;

    public async Task<ConversationTurnContext> OpenTurnAsync(
        string customerExternalId,
        long? knownConversationId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerExternalId);

        var customerId = await OpenCustomerAsync(customerExternalId, utcNow, cancellationToken);
        var conversation = await FindKnownConversationAsync(knownConversationId, customerId, cancellationToken)
            ?? await OpenConversationAsync(customerId, utcNow, cancellationToken);

        return new ConversationTurnContext
        {
            CustomerId = customerId,
            ConversationId = conversation.Id,
            Mode = conversation.Mode,
            ModeRevision = conversation.ModeRevision,
            WindowExpiresAt = conversation.WindowExpiresAt,
        };
    }

    public async Task<ConversationStateDocument> LoadStateAsync(
        long conversationId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ConversationStates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ConversationId == conversationId, cancellationToken);

        if (state is null || ConversationStatePolicy.IsExpired(state.ExpiresAt, utcNow))
        {
            return ConversationStateDocument.Empty;
        }

        return ConversationStateDocument.Deserialize(state.StateJson);
    }

    public Task<IConversationOperation> BeginFinalOperationAsync(
        long conversationId,
        CancellationToken cancellationToken) =>
        coordinator.BeginAsync(conversationId, cancellationToken);

    public async Task AcceptInboundAsync(
        ConversationTurnContext context,
        DateTime providerTimestampUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var conversation = await dbContext.Conversations.FindAsync([context.ConversationId], cancellationToken)
            ?? throw new InvalidOperationException(
                $"The conversation {context.ConversationId} of this turn no longer exists.");

        conversation.LastInboundAt = utcNow;
        conversation.WindowExpiresAt = ConversationWindowPolicy.Refresh(providerTimestampUtc);
        conversation.UpdatedAt = utcNow;

        var customer = await dbContext.Customers.FindAsync([context.CustomerId], cancellationToken);

        if (customer is not null)
        {
            customer.LastSeenAt = utcNow;
        }

        context.WindowExpiresAt = conversation.WindowExpiresAt;
    }

    public async Task SaveStateAsync(
        ConversationTurnContext context,
        ConversationStateDocument state,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        var stored = await dbContext.ConversationStates
            .FindAsync([context.ConversationId], cancellationToken);
        var json = state.ToJson();
        var expiresAt = ConversationStatePolicy.Refresh(utcNow);

        if (stored is null)
        {
            dbContext.ConversationStates.Add(new ConversationState
            {
                ConversationId = context.ConversationId,
                StateJson = json,
                ExpiresAt = expiresAt,
                UpdatedAt = utcNow,
            });

            return;
        }

        // Expired or malformed state is replaced by this write, and every successful write slides the
        // whole document's expiry forward together.
        stored.StateJson = json;
        stored.ExpiresAt = expiresAt;
        stored.UpdatedAt = utcNow;
    }

    public async Task SetModeAsync(
        ConversationTurnContext context,
        string mode,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        var conversation = await dbContext.Conversations.FindAsync([context.ConversationId], cancellationToken)
            ?? throw new InvalidOperationException(
                $"The conversation {context.ConversationId} of this turn no longer exists.");

        var previousMode = conversation.Mode;

        conversation.Mode = mode;
        conversation.UpdatedAt = utcNow;

        // A mode decision this turn really made counts as one more decision for the conversation, so a
        // handoff that was made durable earlier can tell that a later decision has replaced it. A write that
        // leaves the mode as it is decides nothing and adds nothing.
        if (!string.Equals(previousMode, mode, StringComparison.Ordinal))
        {
            conversation.ModeRevision++;
        }

        context.Mode = mode;
        context.ModeRevision = conversation.ModeRevision;
    }

    public Task CommitAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task RecordOutboundAsync(
        long conversationId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.FindAsync([conversationId], cancellationToken);

        if (conversation is null)
        {
            return;
        }

        conversation.LastOutboundAt = utcNow;
        conversation.UpdatedAt = utcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<long> OpenCustomerAsync(
        string customerExternalId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken);

        await using (var insert = Create(connection, InsertCustomerSql))
        {
            Add(insert, "whatsapp_number", customerExternalId);
            Add(insert, "now", utcNow);

            if (await insert.ExecuteScalarAsync(cancellationToken) is long insertedId)
            {
                return insertedId;
            }
        }

        await using var select = Create(connection, SelectCustomerSql);
        Add(select, "whatsapp_number", customerExternalId);

        return await select.ExecuteScalarAsync(cancellationToken) is long existingId
            ? existingId
            : throw new InvalidOperationException("The customer was neither stored nor found.");
    }

    private async Task<Conversation?> FindKnownConversationAsync(
        long? knownConversationId,
        long customerId,
        CancellationToken cancellationToken)
    {
        if (knownConversationId is not { } candidate)
        {
            return null;
        }

        // An inbound envelope may name a conversation, but only the sender's own open conversation is
        // usable; anything else falls back to the active conversation of this customer.
        return await dbContext.Conversations.FirstOrDefaultAsync(
            conversation => conversation.Id == candidate
                && conversation.CustomerId == customerId
                && conversation.Mode != ConversationModes.Closed,
            cancellationToken);
    }

    private async Task<Conversation> OpenConversationAsync(
        long customerId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Conversations.FirstOrDefaultAsync(
            conversation => conversation.CustomerId == customerId
                && conversation.Mode != ConversationModes.Closed,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var connection = await OpenConnectionAsync(cancellationToken);

        await using (var insert = Create(connection, InsertConversationSql))
        {
            Add(insert, "customer_id", customerId);
            Add(insert, "now", utcNow);

            if (await insert.ExecuteScalarAsync(cancellationToken) is long insertedId)
            {
                return await dbContext.Conversations.FirstAsync(
                    conversation => conversation.Id == insertedId,
                    cancellationToken);
            }
        }

        // A concurrent turn of the same customer inserted the active conversation first, so this turn
        // joins that conversation instead of creating a second one.
        await using var select = Create(connection, SelectActiveConversationSql);
        Add(select, "customer_id", customerId);

        var conversationId = await select.ExecuteScalarAsync(cancellationToken) is long existingId
            ? existingId
            : throw new InvalidOperationException("The active conversation was neither stored nor found.");

        return await dbContext.Conversations.FirstAsync(
            conversation => conversation.Id == conversationId,
            cancellationToken);
    }

    private async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private static DbCommand Create(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
