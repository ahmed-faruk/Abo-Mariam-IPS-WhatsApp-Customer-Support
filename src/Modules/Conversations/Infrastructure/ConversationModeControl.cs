using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;

/// <summary>
/// The explicit mode-control seam. Releasing a conversation back to the assistant and closing one are
/// requests an operator makes, never something a customer message can trigger, and a closed
/// conversation is never reopened.
/// </summary>
internal sealed class ConversationModeControl(ConversationDbContext dbContext, TimeProvider clock) : IConversationModeControl
{
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

        if (string.Equals(next, ConversationModes.Closed, StringComparison.Ordinal))
        {
            conversation.ClosedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ConversationModeChangeOutcome.Changed;
    }
}
