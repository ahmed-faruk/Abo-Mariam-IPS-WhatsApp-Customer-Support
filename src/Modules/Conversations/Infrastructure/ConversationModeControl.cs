using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The explicit mode-control seam. Taking a conversation over, releasing it back to the assistant and
/// closing it are requests an operator makes, never something a customer message can trigger, and a
/// closed conversation is never reopened. Every one of them runs inside the conversation's own
/// final-operation lock, so it is ordered against the last authorization of an automatic reply: either
/// the operator's new mode is committed first and the stale turn is suppressed, or the reply is durable
/// first and the operator action applies to the conversation afterwards.
/// </summary>
internal sealed class ConversationModeControl(
    ConversationDbContext dbContext,
    ConversationOperationCoordinator coordinator,
    TimeProvider clock) : IConversationModeControl
{
    public Task<ConversationModeChangeOutcome> TakeOverAsync(
        long conversationId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(conversationId, ConversationModeRules.AfterTakeOver, cancellationToken);

    public Task<ConversationModeChangeOutcome> ReleaseToAiAsync(
        long conversationId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(conversationId, ConversationModeRules.AfterExplicitRelease, cancellationToken);

    public Task<ConversationModeChangeOutcome> CloseAsync(
        long conversationId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(conversationId, ConversationModeRules.AfterClose, cancellationToken);

    private async Task<ConversationModeChangeOutcome> ChangeAsync(
        long conversationId,
        Func<string, string?> decide,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversationId);

        await using var operation = await coordinator.BeginAsync(conversationId, cancellationToken);

        // The mode this decides on is the stored one, read under the conversation's lock, so the change
        // cannot be based on a mode that an automatic turn is authorized on at the same time.
        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(candidate => candidate.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return ConversationModeChangeOutcome.NotFound;
        }

        var next = decide(conversation.Mode);

        if (next is null || string.Equals(next, conversation.Mode, StringComparison.Ordinal))
        {
            return ConversationModeChangeOutcome.Unchanged;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        conversation.Mode = next;
        conversation.UpdatedAt = now;

        // Every explicit operator decision advances the conversation's mode revision, which is what makes a
        // handoff that an earlier attempt made durable recognisable as superseded instead of being applied
        // again on a later retry.
        conversation.ModeRevision++;

        if (string.Equals(next, ConversationModes.Closed, StringComparison.Ordinal))
        {
            conversation.ClosedAt = now;
        }

        // The commit writes the mode change and releases the conversation's lock together.
        await operation.CommitAsync(cancellationToken);

        return ConversationModeChangeOutcome.Changed;
    }
}
