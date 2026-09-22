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
    /// lock space, so a conversation id cannot collide with an unrelated lock of another key space. It is
    /// folded into the key rather than reserving bits of it, because the key has to keep the whole
    /// conversation id: the reserved bits are what used to make 1 and 1 + 2^32 share one lock.
    /// </summary>
    private const long LockNamespace = 0x434F4E56_00000000L;

    /// <summary>
    /// The deterministic advisory lock key of one conversation. Every bit of the id takes part, so two
    /// conversations that merely share their low 32 bits, such as 1 and 1 + 2^32, still hold two different
    /// locks. The exclusive or with the fixed namespace constant maps two different ids to two different
    /// keys, and the key depends on nothing but the id: no process-random hash, no machine architecture
    /// and no per-replica state, so every replica derives the same key for the same conversation.
    /// </summary>
    internal static long LockKeyFor(long conversationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversationId);

        return LockNamespace ^ conversationId;
    }

    /// <summary>
    /// Serializes the final section of one conversation. The caller owns the returned operation and must
    /// dispose it, which releases the lock even when the work between the two calls throws.
    /// </summary>
    internal async Task<IConversationOperation> BeginAsync(
        long conversationId,
        CancellationToken cancellationToken)
    {
        // The key is derived before the transaction, so an id outside the conversation domain fails
        // without ever opening one.
        var lockKey = LockKeyFor(conversationId);

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Waiting here is the whole point: the turn and the operator action are ordered by who took
            // the conversation's lock first, and that ordering is visible to every replica.
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                [lockKey],
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

        public async Task<ConversationModeSnapshot> ReloadModeAsync(CancellationToken cancellationToken)
        {
            // A projection reads the stored value itself: the entity this turn loaded when it started is
            // not consulted, so a mode change committed by another operation after that is seen here. The
            // revision is read in the same projection, so the two always describe one stored decision.
            var stored = await dbContext.Conversations
                .Where(conversation => conversation.Id == conversationId)
                .Select(conversation => new { conversation.Mode, conversation.ModeRevision })
                .FirstOrDefaultAsync(cancellationToken);

            return stored is null
                ? throw new InvalidOperationException(
                    $"The conversation {conversationId} of this operation no longer exists.")
                : new ConversationModeSnapshot(stored.Mode, stored.ModeRevision);
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
