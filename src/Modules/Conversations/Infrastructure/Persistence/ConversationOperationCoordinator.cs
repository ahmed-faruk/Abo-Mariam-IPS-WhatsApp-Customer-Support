using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// The Conversations-owned coordination of one conversation, and the only place the module serializes
/// its final decisions. An inbound turn that is about to enqueue an automatic reply and an explicit
/// operator mode change both run their short final section here, so exactly one of them decides the
/// conversation's mode first and the other sees that decision.
/// </summary>
/// <remarks>
/// The lock is PostgreSQL's own transaction-scoped advisory lock, taken inside the transaction that
/// carries the operation's Conversations changes. It is keyed by the conversation id, it lives in the
/// shared database rather than in a process, and PostgreSQL releases it at the end of the transaction
/// whatever the outcome: a commit, a rollback, an exception or a dropped connection. A pooled
/// connection can therefore never return with the conversation still locked, and no schema column, no
/// second queue and no distributed transaction is needed to make two application replicas agree.
/// </remarks>
internal sealed class ConversationOperationCoordinator(ConversationDbContext dbContext)
{
    /// <summary>
    /// The constant that namespaces this application's conversation locks inside the shared advisory
    /// lock space, so a conversation id cannot collide with an unrelated lock of another key space.
    /// </summary>
    private const long LockNamespace = 0x434F4E56_00000000L;

    /// <summary>The deterministic advisory lock key of one conversation.</summary>
    internal static long LockKeyFor(long conversationId) => LockNamespace | (conversationId & 0xFFFF_FFFFL);

    /// <summary>
    /// Serializes the final section of one conversation. The caller owns the returned operation and must
    /// dispose it, which releases the lock even when the work between the two calls throws.
    /// </summary>
    internal async Task<IConversationOperation> BeginAsync(
        long conversationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversationId);

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Waiting here is the whole point: the turn and the operator action are ordered by who took
            // the conversation's lock first, and that ordering is visible to every replica.
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                [LockKeyFor(conversationId)],
                cancellationToken);
        }
        catch
        {
            await transaction.DisposeAsync();

            throw;
        }

        return new Operation(dbContext, transaction, conversationId);
    }

    private sealed class Operation(
        ConversationDbContext dbContext,
        IDbContextTransaction transaction,
        long conversationId) : IConversationOperation
    {
        private bool committed;

        public async Task<string> ReloadModeAsync(CancellationToken cancellationToken)
        {
            // A projection reads the stored value itself: the entity this turn loaded when it started is
            // not consulted, so a mode change committed by another operation after that is seen here.
            var mode = await dbContext.Conversations
                .Where(conversation => conversation.Id == conversationId)
                .Select(conversation => conversation.Mode)
                .FirstOrDefaultAsync(cancellationToken);

            return mode ?? throw new InvalidOperationException(
                $"The conversation {conversationId} of this operation no longer exists.");
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!committed)
            {
                // Disposing rolls the transaction back, which is also what releases the advisory lock.
                await transaction.RollbackAsync(CancellationToken.None);
            }

            await transaction.DisposeAsync();
        }
    }
}
