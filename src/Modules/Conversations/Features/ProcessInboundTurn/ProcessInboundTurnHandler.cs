using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The inbound orchestration of docs/TECHNICAL.md section 16. One accepted turn is recorded, routed and
/// answered in a fixed order: the Conversations change is committed first, the service window is then
/// re-checked, and only then may the renderer produce text and Messaging store exactly one durable
/// reply. No transaction spans the two modules, so a retried turn is safe: the reply's correlation id
/// is the inbound provider message id, and Messaging refuses to store a second reply for it.
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

        if (route.Intent.Kind == ConversationResponseKind.HumanHandoff)
        {
            await store.SetModeAsync(context, ConversationModes.Human, now, cancellationToken);
        }

        // One commit finishes the Conversations change before Messaging is asked for anything.
        await store.CommitAsync(cancellationToken);

        // The window is evaluated immediately before the free-form enqueue, against the current clock.
        if (!ConversationWindowPolicy.IsOpen(context.WindowExpiresAt, UtcNow()))
        {
            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        var rendered = await renderer.RenderAsync(route.Intent, cancellationToken);

        if (!rendered.IsRendered)
        {
            // The renderer is not bound yet, or produced nothing. No prose means no Outbox row.
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

        await store.RecordOutboundAsync(context.ConversationId, UtcNow(), cancellationToken);

        return Result(context, ConversationTurnOutcome.ResponseEnqueued, outboxMessageId);
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
