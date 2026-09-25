using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;

namespace WhatsAppMonitorAssistant.Host.Web.Admin;

/// <summary>
/// Wires Admin Lite (Issue #14): its options, its Razor Pages under <c>Admin/Pages</c> and the
/// local-port guard that must run before any endpoint.
/// </summary>
public static class AdminLiteRegistration
{
    public static IServiceCollection AddAdminLite(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AdminLiteOptions>()
            .Bind(configuration.GetSection(AdminLiteOptions.ConfigurationSectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdminLiteOptions>, AdminLiteOptionsValidator>();

        services.AddRazorPages(options => options.RootDirectory = "/Admin/Pages");

        // The pages show Arabic catalogue and business text. HTML-significant characters are still
        // encoded; letters of every script are written as themselves instead of as character references.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        return services;
    }

    public static IApplicationBuilder UseAdminLitePortGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<AdminLitePortGuard>();
    }

    public static IEndpointRouteBuilder MapAdminLite(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapRazorPages();

        return endpoints;
    }
}
