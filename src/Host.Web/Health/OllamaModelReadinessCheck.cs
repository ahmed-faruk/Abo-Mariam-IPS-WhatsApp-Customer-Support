using Microsoft.Extensions.Diagnostics.HealthChecks;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Host.Web.Health;

/// <summary>Readiness adapter from the Intelligence model-discovery contract to ASP.NET Core health checks.</summary>
public sealed class OllamaModelReadinessCheck(
    IAiModelReadiness readiness) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = await readiness.CheckAsync(cancellationToken);

        return status == AiModelReadinessStatus.Ready
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Ollama model status: {status}");
    }
}
