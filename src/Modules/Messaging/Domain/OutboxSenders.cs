using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Domain;

/// <summary>Outbox sender values allowed by the <c>ck_outbox_sender</c> constraint.</summary>
public static class OutboxSenders
{
    public const string Ai = OutboundSenderNames.Ai;

    public const string Agent = OutboundSenderNames.Agent;

    public const string System = OutboundSenderNames.System;
}
