namespace WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

/// <summary>Checks whether the configured AI runtime has the frozen model loaded and discoverable.</summary>
public interface IAiModelReadiness
{
    Task<AiModelReadinessStatus> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>The bounded readiness states exposed by the Intelligence module.</summary>
public enum AiModelReadinessStatus
{
    Ready,
    ModelMissing,
    Unavailable,
}
