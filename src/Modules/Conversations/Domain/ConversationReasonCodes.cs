namespace WhatsAppMonitorAssistant.Modules.Conversations.Domain;

/// <summary>
/// The bounded, application-owned reason codes a response intent carries. They explain a deterministic
/// decision to the renderer and to the operator, and they never contain customer text, model text or a
/// commercial fact.
/// </summary>
public static class ConversationReasonCodes
{
    /// <summary>The customer sent a text message with no text in it.</summary>
    public const string EmptyMessage = "EmptyMessage";

    /// <summary>The model never produced output that satisfied the NLU contract.</summary>
    public const string InvalidModelOutput = "InvalidModelOutput";

    /// <summary>The AI runtime is unreachable, or answered too slowly to be trusted.</summary>
    public const string AiUnavailable = "AiUnavailable";

    /// <summary>A stated resolution could not be read as a supported screen resolution.</summary>
    public const string ResolutionNotUnderstood = "ResolutionNotUnderstood";

    /// <summary>The exact model code the customer named is unknown or currently unequipped.</summary>
    public const string ModelCodeNotAvailable = "ModelCodeNotAvailable";

    /// <summary>The referenced product no longer exists in the catalogue.</summary>
    public const string ProductNoLongerAvailable = "ProductNoLongerAvailable";

    /// <summary>A comparison needs at least two candidates from the current shortlist.</summary>
    public const string ComparisonNeedsTwoCandidates = "ComparisonNeedsTwoCandidates";

    /// <summary>The question did not name one of the approved business-info concepts.</summary>
    public const string BusinessInfoKeyNotResolved = "BusinessInfoKeyNotResolved";

    /// <summary>The question named more than one approved business-info concept at once.</summary>
    public const string BusinessInfoKeyAmbiguous = "BusinessInfoKeyAmbiguous";
}
