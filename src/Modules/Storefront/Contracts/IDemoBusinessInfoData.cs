namespace WhatsAppMonitorAssistant.Modules.Storefront.Contracts;

/// <summary>
/// The controlled-demo business-info seed of docs/TECHNICAL.md section 36.5. It inserts the approved
/// keys that have no stored row and never overwrites an existing one; restoring a stored value is the
/// job of <see cref="IStorefrontBusinessInfoUpdates"/>. Only the demo operator tooling registers this
/// contract; the production composition root never does.
/// </summary>
public interface IDemoBusinessInfoData
{
    /// <summary>
    /// Validates every row first, so an unapproved key or a blank Arabic answer writes nothing, then
    /// inserts the missing keys in one transaction and returns how many rows it inserted.
    /// </summary>
    Task<int> EnsureSeedAsync(
        IReadOnlyList<BusinessInfoUpdate> rows,
        CancellationToken cancellationToken = default);
}
