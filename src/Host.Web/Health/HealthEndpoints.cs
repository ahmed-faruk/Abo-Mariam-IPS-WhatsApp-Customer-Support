using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace WhatsAppMonitorAssistant.Host.Web.Health;

/// <summary>Transport-only liveness and readiness endpoints.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")));

        endpoints.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
                ResponseWriter = ReadinessResponseWriter.WriteAsync,
            });

        return endpoints;
    }
}

/// <summary>Liveness payload.</summary>
/// <param name="Status">Current process status.</param>
public sealed record HealthResponse(string Status);
