namespace WhatsAppMonitorAssistant.Modules.Conversations.Contracts;

/// <summary>
/// The renderer-neutral kinds of reply one orchestrated turn can ask for. The kind tells the renderer
/// which deterministic template and which authoritative facts to use; it never carries the wording.
/// </summary>
public enum ConversationResponseKind
{
    /// <summary>A short greeting reply.</summary>
    Greeting,

    /// <summary>A product search answered with a shortlist of catalogue-backed candidates.</summary>
    ProductSearchResults,

    /// <summary>The details of one identified product.</summary>
    ProductDetails,

    /// <summary>A comparison of the referenced shortlist candidates.</summary>
    ProductComparison,

    /// <summary>The current availability of one identified product.</summary>
    Availability,

    /// <summary>The current price of one identified product.</summary>
    Price,

    /// <summary>One stored business-info value, named by its canonical key.</summary>
    BusinessInfo,

    /// <summary>The acknowledgement that a human agent takes over.</summary>
    HumanHandoff,

    /// <summary>The reply that only text is accepted in this demo.</summary>
    UnsupportedMedia,

    /// <summary>The short refusal that redirects an unrelated request.</summary>
    OutOfScope,

    /// <summary>The reply asking the customer to clarify an ambiguous or unresolved request.</summary>
    Clarification,

    /// <summary>The reply used when the AI runtime is unavailable or too slow to be trusted.</summary>
    AiUnavailable,

    /// <summary>The reply used when a valid search found nothing that satisfies the stated filters.</summary>
    NoMatch,
}
