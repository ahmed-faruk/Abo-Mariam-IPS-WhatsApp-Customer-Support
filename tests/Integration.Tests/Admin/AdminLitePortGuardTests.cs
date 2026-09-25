using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Host.Web.Admin;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// The local-port guard on its own: an /admin request passes only when it arrived on the configured
/// admin port through a loopback address, so even an admin listener bound beyond loopback by mistake
/// never serves another machine.
/// </summary>
public sealed class AdminLitePortGuardTests
{
    private const int AdminPort = 5001;

    [Theory]
    [InlineData("127.0.0.1", AdminPort, "/admin/catalog", true)]
    [InlineData("::1", AdminPort, "/admin/catalog", true)]
    [InlineData("::ffff:127.0.0.1", AdminPort, "/admin/catalog", true)]
    [InlineData("192.168.1.10", AdminPort, "/admin/catalog", false)]
    [InlineData("10.0.0.5", AdminPort, "/ADMIN", false)]
    [InlineData("127.0.0.1", 5000, "/admin/catalog", false)]
    [InlineData("192.168.1.10", 5000, "/health/live", true)]
    public async Task Guard_admin_requires_the_admin_port_and_a_loopback_address(
        string localAddress,
        int localPort,
        string path,
        bool passes)
    {
        var reachedNext = false;
        var guard = new AdminLitePortGuard(
            _ =>
            {
                reachedNext = true;

                return Task.CompletedTask;
            },
            Options.Create(new AdminLiteOptions { Port = AdminPort }));
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Parse(localAddress);
        context.Connection.LocalPort = localPort;
        context.Request.Path = path;

        await guard.InvokeAsync(context);

        Assert.Equal(passes, reachedNext);
        Assert.Equal(passes ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, context.Response.StatusCode);
    }
}
