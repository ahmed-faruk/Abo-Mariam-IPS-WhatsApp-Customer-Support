using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WhatsAppMonitorAssistant.Host.Web.Health;

/// <summary>Writes the bounded dependency state required by the controlled-demo readiness endpoint.</summary>
public static class ReadinessResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";

        var checks = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in report.Entries)
        {
            checks[entry.Key] = entry.Value.Status.ToString();
        }

        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["status"] = report.Status.ToString(),
            ["checks"] = checks,
        };

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            payload,
            cancellationToken: context.RequestAborted);
    }
}
