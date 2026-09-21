namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// The structured-NLU intents of docs/TECHNICAL.md section 9. The member names are the wire names:
/// the model must return one of them exactly, and no other intent leaves Infrastructure as a
/// successful result. There is deliberately no Clarification intent — a clarification is an
/// application result status, not something the model routes to.
/// </summary>
public enum NluIntent
{
    /// <summary>A greeting with no other actionable store or product request.</summary>
    Greeting,

    /// <summary>The customer wants to find a monitor, including asking whether the store has a model.</summary>
    ProductSearch,

    /// <summary>The customer asks for specifications of one already identified item.</summary>
    ProductDetails,

    /// <summary>The customer compares two or more candidate items.</summary>
    ProductComparison,

    /// <summary>The customer asks whether an identified item is still available.</summary>
    AvailabilityCheck,

    /// <summary>The customer asks the price of an identified item.</summary>
    PriceCheck,

    /// <summary>A store-level question such as working hours, address or delivery policy.</summary>
    BusinessInfo,

    /// <summary>The customer asks to speak to a human.</summary>
    HumanHandoff,

    /// <summary>Reserved for unsupported media input, never for ordinary unusual text.</summary>
    UnsupportedMedia,

    /// <summary>A request unrelated to used-monitor sales or store support.</summary>
    OutOfScope,
}
