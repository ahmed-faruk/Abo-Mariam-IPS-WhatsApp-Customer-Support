using System.Net;
using Microsoft.Extensions.Options;

namespace WhatsAppMonitorAssistant.Host.Web.Admin;

/// <summary>
/// The local-port guard of docs/TECHNICAL.md section 36.2. Every <c>/admin</c> request is answered as
/// not found unless it arrived on the configured Admin Lite port through a loopback address, so neither
/// the public listener that the tunnel exposes nor another machine can reach Admin Lite, whatever host
/// name or path spelling a request uses. This is a controlled-demo exposure decision, not a pilot or
/// production security pattern.
/// </summary>
public sealed class AdminLitePortGuard(RequestDelegate next, IOptions<AdminLiteOptions> options)
{
    private static readonly PathString AdminPath = new("/admin");

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase)
            && (options.Value.Port is not { } port
                || context.Connection.LocalPort != port
                || !IsLoopback(context.Connection.LocalIpAddress)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsLoopback(IPAddress? address) =>
        address is not null
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
