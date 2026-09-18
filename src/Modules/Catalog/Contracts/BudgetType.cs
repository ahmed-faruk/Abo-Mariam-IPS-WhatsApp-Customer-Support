namespace WhatsAppMonitorAssistant.Modules.Catalog.Contracts;

/// <summary>
/// How a customer budget constrains a search. The values mirror the budget contract of
/// docs/TECHNICAL.md section 8.3.
/// </summary>
public enum BudgetType
{
    /// <summary>The customer stated no budget, so no price constraint is applied.</summary>
    None,

    /// <summary>
    /// A preference around <see cref="ProductBudget.Target"/>. A configurable tolerance is applied,
    /// so the resolved maximum is above the target but the result is still priced near it.
    /// </summary>
    Soft,

    /// <summary>
    /// A ceiling. Nothing above <see cref="ProductBudget.Target"/> may ever be returned, which is the
    /// invariant of docs/TECHNICAL.md section 11.
    /// </summary>
    Hard,

    /// <summary>An explicit inclusive range between <see cref="ProductBudget.Min"/> and <see cref="ProductBudget.Max"/>.</summary>
    Range,
}
