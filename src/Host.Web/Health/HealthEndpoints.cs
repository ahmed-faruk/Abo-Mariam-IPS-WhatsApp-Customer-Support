namespace WhatsAppMonitorAssistant.Host.Web.Health;

/// <summary>
/// Transport-only health endpoints. Readiness checks for PostgreSQL and Ollama are
/// added by the tickets that introduce those dependencies.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")));

        return endpoints;
    }
}

/// <summary>Liveness payload.</summary>
/// <param name="Status">Current process status.</param>
public sealed record HealthResponse(string Status);
