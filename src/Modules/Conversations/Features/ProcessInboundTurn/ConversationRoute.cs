using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Features.ProcessInboundTurn;

/// <summary>
/// The deterministic decision of one turn: what the customer should be told, and the UX state the
/// decision leaves behind. The state never carries a commercial fact, so the renderer reloads them.
/// </summary>
/// <param name="Intent">The renderer-neutral reply the turn decided on.</param>
/// <param name="State">The state after the turn.</param>
internal sealed record ConversationRoute(ConversationResponseIntent Intent, ConversationStateDocument State);
