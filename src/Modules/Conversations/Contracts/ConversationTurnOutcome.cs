namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>What orchestrating one inbound turn achieved.</summary>
public enum ConversationTurnOutcome
{
    /// <summary>Exactly one durable Outbox message was enqueued, or an existing one was reused.</summary>
    ResponseEnqueued,

    /// <summary>
    /// The turn was accepted and persisted, but nothing may be sent: the service window is closed, no
    /// renderer is bound, or the customer is already waiting for a human agent.
    /// </summary>
    NoResponse,

    /// <summary>The conversation is in Human mode, so the message was recorded without any AI reply.</summary>
    AwaitingHuman,
}
