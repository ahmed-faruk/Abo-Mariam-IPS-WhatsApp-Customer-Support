using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The deterministic decision of one turn: what the customer should be told, and the UX state the
/// decision leaves behind. The state never carries a commercial fact, so the renderer reloads them.
/// </summary>
/// <param name="Intent">The renderer-neutral reply the turn decided on.</param>
/// <param name="State">
/// The state of the turn before anything is sent: the customer's own context, such as the effective
/// filters of this search, plus every part of the UX state the customer was already shown.
/// </param>
/// <param name="DisplayedState">
/// The state that only becomes true once the reply of this turn is durably accepted by the Outbox: the
/// list the reply shows and the product that list puts first. It is null when the turn displays nothing
/// new, so nothing about a product the customer never saw can become reference-resolvable.
/// </param>
internal sealed record ConversationRoute(
    ConversationResponseIntent Intent,
    ConversationStateDocument State,
    ConversationStateDocument? DisplayedState = null);
