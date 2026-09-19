using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure;

/// <summary>
/// The only member of Intelligence Infrastructure that the composition root calls. It wires the frozen
/// AI profile, the schema, the Ollama transport and the structured-NLU contract that later modules
/// consume through <see cref="IAiNluClient"/>.
/// </summary>
public static class IntelligenceModuleRegistration
{
    public static IServiceCollection AddIntelligenceModule(
        this IServiceCollection services,
        Action<OllamaAiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var aiOptions = services.AddOptions<OllamaAiOptions>()
            .Configure(options => configure?.Invoke(options));

        aiOptions.Services.AddSingleton<IValidateOptions<OllamaAiOptions>, OllamaAiOptionsValidator>();

        // An incompatible AI profile is rejected at startup, so the host can never run a model
        // configuration the project did not measure.
        aiOptions.ValidateOnStart();

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<OllamaAiOptions>>().Value);
        services.AddSingleton(NluOutputSchema.Load());

        services.AddHttpClient<OllamaChatTransport>((provider, client) =>
        {
            var ai = provider.GetRequiredService<OllamaAiOptions>();
            client.BaseAddress = new Uri(ai.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

            // The adapter owns the AI timeout through a linked token, so the transport timeout must
            // never be the thing that cancels a request.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddTransient<IOllamaChatTransport>(provider => provider.GetRequiredService<OllamaChatTransport>());
        services.AddTransient<OllamaChatRequestBuilder>();
        services.AddScoped<IAiNluClient, OllamaAiNluClient>();

        return services;
    }
}
