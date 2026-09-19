namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>
/// How the customer stated a budget, using the values of the structured output of
/// docs/TECHNICAL.md section 8.3. The names are the wire names accepted from the model.
/// </summary>
public enum NluBudgetType
{
    /// <summary>No budget was stated, so every budget field is null.</summary>
    None,

    /// <summary>An approximate budget around <see cref="NluInterpretation.BudgetTarget"/>.</summary>
    Soft,

    /// <summary>A ceiling: <see cref="NluInterpretation.BudgetTarget"/> is the exact maximum.</summary>
    Hard,

    /// <summary>An explicit span between <see cref="NluInterpretation.BudgetMin"/> and <see cref="NluInterpretation.BudgetMax"/>.</summary>
    Range,
}
