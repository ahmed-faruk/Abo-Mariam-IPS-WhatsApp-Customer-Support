using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Host;

internal sealed class ApplicationHostFactory(
    string connectionString,
    HttpMessageHandler ollamaTagsHandler,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    internal const string OllamaTagsClientName = "WhatsAppMonitorAssistant.Intelligence.OllamaTags";
    internal const string OllamaInferenceClientName = "OllamaChatTransport";

    public RecordingMetaHttpMessageHandler MetaHandler { get; } = new();

    public RecordingInferenceHttpMessageHandler InferenceHandler { get; } = new();

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

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(OllamaTagsClientName)
                .ConfigurePrimaryHttpMessageHandler(() => ollamaTagsHandler);

            services.AddHttpClient(OllamaInferenceClientName)
                .ConfigurePrimaryHttpMessageHandler(() => InferenceHandler);

            services.AddHttpClient(nameof(IOutboundMessageSender))
                .ConfigurePrimaryHttpMessageHandler(() => MetaHandler);

            configureServices?.Invoke(services);
        });
    }
}
