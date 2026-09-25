using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Host;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// The real Host.Web on real Kestrel with the two loopback listeners of docs/TECHNICAL.md section 36.2:
/// a public port and an admin port, so a test observes the local-port guard exactly as the tunnel and
/// the operator's browser would. Ollama and Meta stay faked at their HTTP boundaries.
/// </summary>
internal sealed class AdminLiteHostFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    private readonly string? adminPortSetting;
    private readonly Action<IServiceCollection>? configureServices;

    public AdminLiteHostFactory(
        string connectionString,
        bool configureAdminPort = true,
        string? adminPortOverride = null,
        Action<IServiceCollection>? configureServices = null)
    {
        this.connectionString = connectionString;
        this.configureServices = configureServices;

        PublicPort = FreeLoopbackPort();
        AdminPort = FreeLoopbackPort();
        adminPortSetting = adminPortOverride ?? (configureAdminPort ? AdminPort.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);

        UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, PublicPort);
            options.Listen(IPAddress.Loopback, AdminPort);
        });
    }

    public int PublicPort { get; }

    public int AdminPort { get; }

    public HttpClient PublicClient() => Client(PublicPort);

    public HttpClient AdminClient() => Client(AdminPort);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Catalog:Search:SizeToleranceInches", "0.5");
        builder.UseSetting("Catalog:Search:SoftBudgetTolerance", "0.10");
        builder.UseSetting("WhatsApp:ApiVersion", "v23.0");
        builder.UseSetting("WhatsApp:PhoneNumberId", "123456789");
        builder.UseSetting("WhatsApp:WabaId", "987654321");
        builder.UseSetting("WhatsApp:VerifyToken", "test-verify-token");
        builder.UseSetting("WhatsApp:AppSecret", "test-app-secret");
        builder.UseSetting("WhatsApp:AccessToken", "test-access-token");
        builder.UseSetting("WhatsApp:WebhookPermitLimit", "1000");

        if (adminPortSetting is not null)
        {
            builder.UseSetting("AdminLite:Port", adminPortSetting);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(ApplicationHostFactory.OllamaTagsClientName)
                .ConfigurePrimaryHttpMessageHandler(() => StubOllamaTagsHandler.WithNames("qwen3.5:2b-q4_K_M"));
            services.AddHttpClient(ApplicationHostFactory.OllamaInferenceClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new RecordingInferenceHttpMessageHandler());
            services.AddHttpClient(nameof(IOutboundMessageSender))
                .ConfigurePrimaryHttpMessageHandler(() => new RecordingMetaHttpMessageHandler());

            configureServices?.Invoke(services);
        });
    }

    /// <summary>A client that keeps cookies and never follows redirects, so a test sees each answer.</summary>
    private static HttpClient Client(int port) =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
