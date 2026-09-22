using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The deterministic decision of one turn: what the customer should be told, and the UX context the
/// decision leaves behind. The state never carries a commercial fact, so the renderer reloads them.
/// </summary>
/// <param name="Intent">The renderer-neutral reply the turn decided on.</param>
/// <param name="State">
/// The customer-derived state of the turn, which may be persisted as soon as the turn is accepted: the
/// customer's own context, such as the effective filters of this search, plus every part of the UX state
/// the customer was already shown. Everything a reply displays is deliberately absent here, because a
/// list becomes addressable only once the reply that showed it is durably accepted.
/// </param>
internal sealed record ConversationRoute(
    ConversationResponseIntent Intent,
    ConversationStateDocument State);
