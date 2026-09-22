using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>The mode values allowed by the <c>conversations.conversation</c> mode constraint.</summary>
public static class ConversationModes
{
    public const string Ai = "AI";

    public const string Human = "Human";

    public const string Closed = "Closed";

    /// <summary>The model-neutral mode of a stored mode value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of the three stored modes.</exception>
    public static ConversationMode ToContract(string mode) => mode switch
    {
        Ai => ConversationMode.Ai,
        Human => ConversationMode.Human,
        Closed => ConversationMode.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The value is not a stored conversation mode."),
    };

    /// <summary>The stored mode value of a model-neutral mode.</summary>
    public static string FromContract(ConversationMode mode) => mode switch
    {
        ConversationMode.Ai => Ai,
        ConversationMode.Human => Human,
        ConversationMode.Closed => Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The value is not a conversation mode."),
    };
}
