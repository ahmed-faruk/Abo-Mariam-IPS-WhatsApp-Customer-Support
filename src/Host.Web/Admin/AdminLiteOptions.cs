using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace WhatsAppMonitorAssistant.Host.Web.Admin;

/// <summary>
/// The Admin Lite listener of docs/TECHNICAL.md section 36.2. Admin Lite answers only on the local port
/// configured here; with no port configured, every <c>/admin</c> request is not found.
/// </summary>
public sealed class AdminLiteOptions
{
    public const string ConfigurationSectionName = "AdminLite";

    /// <summary>The local port of the loopback admin listener, for example 5001.</summary>
    public int? Port { get; set; }
}

/// <summary>
/// Fails startup unless Admin Lite has its own loopback-only listener. An unset port is accepted and
/// keeps /admin unreachable. A set port must be a non-privileged TCP port served by the
/// <c>Kestrel:Endpoints:Admin:Url</c> endpoint on a loopback address, and no other configured listener
/// may use it, so a mistyped setting can never put the unauthenticated pages on the tunnelled listener
/// or on a network address.
/// </summary>
public sealed partial class AdminLiteOptionsValidator(IConfiguration configuration) : IValidateOptions<AdminLiteOptions>
{
    private const string AdminEndpointKey = "Kestrel:Endpoints:Admin:Url";

    public ValidateOptionsResult Validate(string? name, AdminLiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Port is not { } port)
        {
            return ValidateOptionsResult.Success;
        }

        if (port is < 1024 or > 65535)
        {
            return Fail($"AdminLite:Port must be unset or between 1024 and 65535, but it is {port}.");
        }

        var adminUrl = configuration[AdminEndpointKey];

        if (string.IsNullOrWhiteSpace(adminUrl))
        {
            return Fail($"AdminLite:Port requires {AdminEndpointKey}, the loopback admin listener.");
        }

        if (!Uri.TryCreate(adminUrl, UriKind.Absolute, out var admin)
            || admin.Scheme != Uri.UriSchemeHttp
            || !IsLoopbackHost(admin.Host))
        {
            return Fail($"{AdminEndpointKey} must be a loopback http URL (127.0.0.1, [::1] or localhost), but it is '{adminUrl}'.");
        }

        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            if (endpoint.Key != "Admin" && PortOf(endpoint["Url"]) == port)
            {
                return Fail($"AdminLite:Port {port} is also used by Kestrel:Endpoints:{endpoint.Key}:Url; the admin listener must be the only one on that port.");
            }
        }

        foreach (var url in (configuration["urls"] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PortOf(url) == port)
            {
                return Fail($"AdminLite:Port {port} is also used by the 'urls' setting; the admin listener must be the only one on that port.");
            }
        }

        return admin.Port == port
            ? ValidateOptionsResult.Success
            : Fail($"{AdminEndpointKey} must use AdminLite:Port {port}, but it uses {admin.Port}.");
    }

    private static ValidateOptionsResult Fail(string message) => ValidateOptionsResult.Fail(message);

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>The port of a listener URL, including wildcard hosts such as <c>http://*:5000</c>.</summary>
    private static int? PortOf(string? url)
    {
        var match = url is null ? Match.Empty : UrlPort().Match(url);

        return match.Success ? int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@":(?<port>[0-9]{1,5})(/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPort();
}
