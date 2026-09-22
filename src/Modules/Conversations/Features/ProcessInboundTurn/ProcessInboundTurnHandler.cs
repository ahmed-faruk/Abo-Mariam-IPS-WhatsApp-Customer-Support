using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The inbound orchestration of docs/TECHNICAL.md section 16. One accepted turn is recorded, routed and
/// answered in a fixed order: the Conversations change is committed first, the service window is then
/// re-checked, and only then may the renderer produce text and Messaging store exactly one durable reply.
/// The last step - read the conversation's mode again and decide whether this turn may still answer -
/// runs inside the conversation's final-operation lock, which the operator's own mode changes take as
/// well, so a reply can never be produced for a conversation an operator has already closed or taken
/// over. No transaction spans the two modules, so a retried turn is safe: the reply's correlation id is
/// the inbound provider message id, and Messaging refuses to store a second reply for it.
/// </summary>
internal sealed class ProcessInboundTurnHandler(
    IConversationTurnStore store,
    ConversationIntentRouter router,
    IAiNluClient nlu,
    IConversationRenderer renderer,
    IOutboundMessageQueue outbox,
    TimeProvider clock) : IProcessInboundTurn
{
    /// <summary>The only provider message type the demo interprets. Everything else is unsupported media.</summary>
    internal const string SupportedMessageType = "text";

    public async Task<ConversationTurnResult> ProcessAsync(
        InboundTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        RequireText(turn.ProviderMessageId, nameof(turn.ProviderMessageId));
        RequireText(turn.CustomerExternalId, nameof(turn.CustomerExternalId));
        RequireText(turn.MessageType, nameof(turn.MessageType));

        var now = UtcNow();
        var context = await store.OpenTurnAsync(
            turn.CustomerExternalId,
            turn.ConversationId,
            now,
            cancellationToken);

        // An accepted inbound always refreshes the lifecycle and the 24-hour service window, whatever
        // the mode is, because the window is what later allows a free-form reply at all.
        await store.AcceptInboundAsync(context, ConversationTimestampOf(turn), now, cancellationToken);

        if (!ConversationModeRules.AnswersAutomatically(context.Mode))
        {
            // A human owns this conversation, so the message is recorded and nothing else happens: no
            // interpretation, no renderer, no Outbox. Only an explicit release returns it to AI.
            await store.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.AwaitingHuman);
        }

        var state = await store.LoadStateAsync(context.ConversationId, now, cancellationToken);
        var route = await DecideAsync(context, turn, state, cancellationToken);

        if (route.State != state)
        {
            await store.SaveStateAsync(context, route.State, now, cancellationToken);
        }

        // One commit finishes the Conversations change before Messaging is asked for anything. Only the
        // customer's own context and what the customer already saw is written here: the list this turn
        // would display is committed after the Outbox accepts the reply that displays it.
        await store.CommitAsync(cancellationToken);

        var handoff = route.Intent.Kind == ConversationResponseKind.HumanHandoff;

        // The window is evaluated immediately before the free-form enqueue, against the current clock.
        if (!ConversationWindowPolicy.IsOpen(context.WindowExpiresAt, UtcNow()))
        {
            // No free-form reply may be sent, but a handoff does not need one: the customer asked for a
            // human, and that durable answer is recorded even when the window is closed.
            if (handoff)
            {
                await AuthorizeAsync(
                    context,
                    ConversationModeRules.AfterHandoff,
                    cancellationToken);
            }

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        var rendered = await renderer.RenderAsync(route.Intent, cancellationToken);

        if (!rendered.IsRendered)
        {
            // The renderer is not bound yet, or produced nothing. No prose means no Outbox row.
            if (handoff)
            {
                await AuthorizeAsync(
                    context,
                    ConversationModeRules.AfterHandoff,
                    cancellationToken);
            }

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        // The final authorization. Everything before this point may have taken a long time, so the mode
        // captured when the turn started is not trusted any more: the current mode is read again inside
        // the conversation's lock, and only an automatic conversation may still produce this reply.
        await using var operation = await store.BeginFinalOperationAsync(
            context.ConversationId,
            cancellationToken);
        var authorizedMode = await operation.ReloadModeAsync(cancellationToken);
        context.Mode = authorizedMode;

        if (!ConversationModeRules.AnswersAutomatically(authorizedMode))
        {
            // An operator closed the conversation or took it over while this turn was being prepared. The
            // reply is dropped rather than enqueued: nothing is sent on behalf of a conversation the
            // operator now owns, and the operator's own committed mode is left exactly as it is.
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        var outboxMessageId = await outbox.EnqueueAsync(
            new OutboundMessageRequest(
                context.ConversationId,
                turn.CustomerExternalId,
                turn.ProviderMessageId,
                OutboundSenderNames.Ai,
                rendered.Body!),
            cancellationToken);

        if (handoff)
        {
            // The acknowledgement is durable now, so the conversation may become Human. A crash between
            // the two is safe: the retried turn reuses this same Outbox row and completes the change.
            await store.SetModeAsync(context, ConversationModes.Human, UtcNow(), cancellationToken);
        }

        if (route.DisplayedState is { } displayed)
        {
            // The reply the customer will receive is durable, so the list it shows becomes the list the
            // conversation can reference from now on.
            await store.SaveStateAsync(context, displayed, UtcNow(), cancellationToken);
        }

        await store.RecordOutboundAsync(context.ConversationId, UtcNow(), cancellationToken);
        await operation.CommitAsync(cancellationToken);

        return Result(context, ConversationTurnOutcome.ResponseEnqueued, outboxMessageId);
    }

    /// <summary>
    /// Applies the one durable Conversations change of a final operation, inside the conversation's
    /// lock: the mode is read again, and the change only happens if it still applies to the mode that is
    /// stored now. A conversation an operator closed stays closed, and a mode the operator already chose
    /// is never overwritten by a stale turn.
    /// </summary>
    private async Task AuthorizeAsync(
        ConversationTurnContext context,
        Func<string, string?> decide,
        CancellationToken cancellationToken)
    {
        await using var operation = await store.BeginFinalOperationAsync(
            context.ConversationId,
            cancellationToken);
        var currentMode = await operation.ReloadModeAsync(cancellationToken);
        context.Mode = currentMode;

        if (decide(currentMode) is { } next
            && !string.Equals(next, currentMode, StringComparison.Ordinal))
        {
            await store.SetModeAsync(context, next, UtcNow(), cancellationToken);
        }

        await operation.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Decides what the turn should be told. Unsupported media and an empty text message are answered
    /// without interpretation, and only a text message in AI mode ever reaches Intelligence.
    /// </summary>
    private async Task<ConversationRoute> DecideAsync(
        ConversationTurnContext context,
        InboundTurn turn,
        ConversationStateDocument state,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(turn.MessageType, SupportedMessageType, StringComparison.OrdinalIgnoreCase))
        {
            return new ConversationRoute(
                Intent(context, turn.CustomerExternalId, ConversationResponseKind.UnsupportedMedia),
                state);
        }

        if (string.IsNullOrWhiteSpace(turn.Body))
        {
            return new ConversationRoute(
                Intent(
                    context,
                    turn.CustomerExternalId,
                    ConversationResponseKind.Clarification,
                    ConversationReasonCodes.EmptyMessage),
                state);
        }

        // The frozen prompt of docs/TECHNICAL.md section 8.2 interprets a single text turn, so the
        // context passed here is deliberately empty: reference resolution is deterministic and runs in
        // this module against the stored UX state instead of being sent to the model.
        var analysis = await nlu.AnalyzeAsync(turn.Body, NluConversationContext.Empty, cancellationToken);

        return await router.RouteAsync(
            context.ConversationId,
            turn.CustomerExternalId,
            turn.Body,
            analysis,
            state,
            cancellationToken);
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;

    private static DateTime ConversationTimestampOf(InboundTurn turn) =>
        ConversationTimestamps.ToUtc(turn.ProviderTimestamp);

    private static ConversationTurnResult Result(
        ConversationTurnContext context,
        ConversationTurnOutcome outcome,
        long? outboxMessageId = null) =>
        new(
            context.ConversationId,
            ConversationModes.ToContract(context.Mode),
            outcome,
            outboxMessageId);

    private static ConversationResponseIntent Intent(
        ConversationTurnContext context,
        string customerExternalId,
        ConversationResponseKind kind,
        string? reasonCode = null) =>
        new()
        {
            Kind = kind,
            ConversationId = context.ConversationId,
            CustomerExternalId = customerExternalId,
            ReasonCode = reasonCode,
        };

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value must not be empty.", parameterName);
        }
    }
}
