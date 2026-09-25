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

/// <summary>Accepts an unset port or a non-privileged TCP port, and nothing else.</summary>
public sealed class AdminLiteOptionsValidator : IValidateOptions<AdminLiteOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminLiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Port is null or (>= 1024 and <= 65535)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"AdminLite:Port must be unset or between 1024 and 65535, but it is {options.Port}.");
    }
}
