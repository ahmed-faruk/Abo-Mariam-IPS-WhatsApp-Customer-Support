using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The inbound orchestration of docs/TECHNICAL.md section 16. One accepted turn is recorded, routed and
/// answered in a fixed order: the Conversations change is committed first, the service window is then
/// re-checked, and only then may the renderer produce text and Messaging store exactly one durable reply.
/// The final section runs inside the conversation's final-operation lock, which the operator's own mode
/// changes take as well: the mode is read again there, the renderer re-reads the current facts there, and
/// the reply is stored there, so a reply can never be produced for a conversation an operator has already
/// closed or taken over and no reply can quote a price that changed while the turn was being prepared. No
/// transaction spans the two modules, so a retried turn is safe: the reply's correlation id is the inbound
/// provider message id, Messaging refuses to store a second reply for it, and a turn whose reply was
/// already stored reconciles that immutable reply instead of rendering a new one. That reconciliation runs
/// before the mode and window gates, because a reply that is already durable is not a new free-form send.
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
            // A human owns this conversation, so a new message is recorded and nothing else happens: no
            // interpretation, no renderer, no new Outbox. Only an explicit release returns it to AI.
            await store.CommitAsync(cancellationToken);

            return await AwaitHumanAsync(context, turn, cancellationToken);
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

        // The final section. Everything before this point may have taken a long time, so the mode captured
        // when the turn started is not trusted any more: the current mode is read again inside the
        // conversation's lock, and only an automatic conversation may still produce a new reply.
        await using var operation = await store.BeginFinalOperationAsync(
            context.ConversationId,
            cancellationToken);
        var authorizedMode = await operation.ReloadModeAsync(cancellationToken);
        context.Mode = authorizedMode;

        // An earlier attempt of this same turn may already have stored its reply. That reply is immutable
        // and is what the customer received, so the retry reconciles it instead of rendering again from
        // facts that may have changed in the meantime. It is not a new free-form send: neither the mode the
        // operator chose since nor a window that has closed since may make the application forget a reply
        // it already durably accepted, so this is checked before both of those gates below.
        if (await outbox.FindByCorrelationAsync(turn.ProviderMessageId, cancellationToken) is { } accepted)
        {
            await ReconcileAsync(
                context,
                route.State,
                accepted,
                applyStoredModeEffect: true,
                cancellationToken);
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.ResponseEnqueued, accepted.OutboxMessageId);
        }

        if (!ConversationModeRules.AnswersAutomatically(authorizedMode))
        {
            // An operator closed the conversation or took it over while this turn was being prepared. The
            // new reply is dropped rather than enqueued: nothing is sent on behalf of a conversation the
            // operator now owns, and the operator's own committed mode is left exactly as it is.
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        // The window is evaluated immediately before the free-form enqueue, against the current clock. The
        // already stored reply was handled above, so reaching this point means this is a new free-form send.
        if (!ConversationWindowPolicy.IsOpen(context.WindowExpiresAt, UtcNow()))
        {
            // No free-form reply may be sent, but a handoff does not need one: the customer asked for a
            // human, and that durable answer is recorded even when the window is closed.
            await EnterHumanWithoutReplyAsync(context, handoff, cancellationToken);
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        // The render re-reads the current business facts of the reply, which is why it runs inside the
        // lock and as late as possible before the durable enqueue.
        var rendered = await renderer.RenderAsync(route.Intent, cancellationToken);

        if (!rendered.IsRendered)
        {
            // No prose means no Outbox row. A handoff does not depend on one: the customer asked for a
            // human, and that durable answer is recorded even when no acknowledgement could be written.
            await EnterHumanWithoutReplyAsync(context, handoff, cancellationToken);
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        // Reading the current facts takes time of its own, so the window is evaluated once more against
        // the current clock immediately before the free-form enqueue. A window that closed while the reply
        // was being rendered must not carry a free-form message, and the list it would have shown must not
        // become addressable either.
        if (!ConversationWindowPolicy.IsOpen(context.WindowExpiresAt, UtcNow()))
        {
            await EnterHumanWithoutReplyAsync(context, handoff, cancellationToken);
            await operation.CommitAsync(cancellationToken);

            return Result(context, ConversationTurnOutcome.NoResponse);
        }

        var metadata = ConversationOutboxMetadata.For(rendered.DisplayedCandidates, handoff);
        var stored = await outbox.EnqueueAsync(
            new OutboundMessageRequest(
                context.ConversationId,
                turn.CustomerExternalId,
                turn.ProviderMessageId,
                OutboundSenderNames.Ai,
                rendered.Body!,
                metadata.ToJson()),
            cancellationToken);

        // The acceptance Messaging returned is authoritative: when the correlation was already stored, the
        // conversation reconciles the reply that was really accepted and not the one this attempt built.
        await ReconcileAsync(
            context,
            route.State,
            stored,
            applyStoredModeEffect: true,
            cancellationToken);
        await operation.CommitAsync(cancellationToken);

        return Result(context, ConversationTurnOutcome.ResponseEnqueued, stored.OutboxMessageId);
    }

    /// <summary>
    /// Records what a conversation a human owns does with one accepted inbound. Normally that is nothing
    /// further: the message is recorded and no interpretation, renderer or reply follows it.
    /// </summary>
    /// <remarks>
    /// An earlier attempt of this same inbound may however already have stored its reply, and a reply that
    /// is already durable is not a new free-form send. The operator's mode suppresses a new automatic
    /// response; it may not make the conversation forget a response it already accepted. So the
    /// conversation's lock is taken, the mode is read again there, and a stored reply of this correlation is
    /// reconciled from its immutable metadata alone. Nothing is interpreted, rendered, enqueued, or checked
    /// against the service window on this path.
    /// </remarks>
    private async Task<ConversationTurnResult> AwaitHumanAsync(
        ConversationTurnContext context,
        InboundTurn turn,
        CancellationToken cancellationToken)
    {
        await using var operation = await store.BeginFinalOperationAsync(
            context.ConversationId,
            cancellationToken);
        context.Mode = await operation.ReloadModeAsync(cancellationToken);

        if (await outbox.FindByCorrelationAsync(turn.ProviderMessageId, cancellationToken) is not { } accepted)
        {
            // Nothing of this inbound is durable, so the documented behaviour stands: the message is
            // recorded, a human owns the conversation, and the lock is released without committing.
            return Result(context, ConversationTurnOutcome.AwaitingHuman);
        }

        // Only the stored reply of this correlation is applied, and only through the same rules the
        // display path uses: the list it really showed and the outbound lifecycle. The mode is a different
        // matter here - the inbound was accepted by a conversation a human owned, so the mode the operator
        // chooses meanwhile is always the stronger decision and the stored reply never moves it.
        var state = await store.LoadStateAsync(context.ConversationId, UtcNow(), cancellationToken);

        await ReconcileAsync(
            context,
            state,
            accepted,
            applyStoredModeEffect: false,
            cancellationToken);
        await operation.CommitAsync(cancellationToken);

        return Result(context, ConversationTurnOutcome.ResponseEnqueued, accepted.OutboxMessageId);
    }

    /// <summary>
    /// Applies the Conversations change of one durably accepted reply: the list its stored metadata
    /// confirms was displayed and the outbound lifecycle, and - where this turn owns the automatic
    /// decision - the mode its stored metadata confirms.
    /// </summary>
    /// <remarks>
    /// The stored metadata is the only evidence of what the accepted reply was. A row stored before this
    /// version carries none, and this attempt's freshly computed route is not proof of what that older
    /// immutable body represented, so a null payload never changes the mode or the displayed list: its row
    /// is still reused, but the conversation keeps the mode and the references the operator last chose.
    /// </remarks>
    /// <param name="applyStoredModeEffect">
    /// True when this reconciliation completes the mode effect of a reply this turn may still act on, which
    /// is a turn that started automatic. False when the inbound was accepted by a conversation a human
    /// already owned: the operator's own mode decision is the stronger one there, so a stored handoff may
    /// never move that conversation - it can neither take it back from an explicit release to the assistant
    /// nor reopen it.
    /// </param>
    private async Task ReconcileAsync(
        ConversationTurnContext context,
        ConversationStateDocument baseState,
        OutboundAcceptance accepted,
        bool applyStoredModeEffect,
        CancellationToken cancellationToken)
    {
        var metadata = ConversationOutboxMetadata.Parse(accepted.ApplicationMetadata);

        // The acknowledgement is durable now, so the conversation may become Human. A crash between the
        // enqueue and this commit is safe: the retried turn reuses this same Outbox row and reads the same
        // effect from the metadata it stored with it. A handoff never reopens a conversation an operator has
        // closed in the meantime, and never overwrites the mode an operator has already chosen.
        if (applyStoredModeEffect
            && metadata is { EntersHumanMode: true }
            && ConversationModeRules.AfterHandoff(context.Mode) is { } handoffMode
            && !string.Equals(handoffMode, context.Mode, StringComparison.Ordinal))
        {
            await store.SetModeAsync(context, handoffMode, UtcNow(), cancellationToken);
        }

        if (metadata is { DisplayedCandidates.Count: > 0 })
        {
            // The reply the customer will receive is durable, so the list it shows becomes the list the
            // conversation can reference from now on: the accepted order, the accepted first product, and
            // the customer's own filters alongside them.
            var first = metadata.DisplayedCandidates[0];

            await store.SaveStateAsync(
                context,
                baseState with
                {
                    Shortlist = ConversationStateDocument.BuildShortlist(
                        metadata.DisplayedCandidates.Select(candidate => (candidate.ModelId, candidate.VariantId))),
                    LastModelId = first.ModelId,
                    LastVariantId = first.VariantId,
                },
                UtcNow(),
                cancellationToken);
        }

        await store.RecordOutboundAsync(context.ConversationId, UtcNow(), cancellationToken);
    }

    /// <summary>
    /// Records the mode of a turn that produced no reply at all. A handoff still has to become durable
    /// Human - with a closed service window, or with no renderer bound, there is simply no acknowledgement
    /// to store first - while every other turn leaves the mode exactly as the operator left it.
    /// </summary>
    private async Task EnterHumanWithoutReplyAsync(
        ConversationTurnContext context,
        bool handoff,
        CancellationToken cancellationToken)
    {
        if (handoff)
        {
            await store.SetModeAsync(context, ConversationModes.Human, UtcNow(), cancellationToken);
        }
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
