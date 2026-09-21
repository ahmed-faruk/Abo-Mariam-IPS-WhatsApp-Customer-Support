using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The module-owned registration is the only thing the composition root calls, so it must resolve the
/// public contract and apply the frozen profile validator without Host.Web knowing module internals.
/// </summary>
public sealed class IntelligenceModuleRegistrationTests
{
    [Fact]
    public void The_registration_resolves_the_public_nlu_contract_and_the_frozen_profile()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiNluClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOllamaChatTransport>());

        var options = provider.GetRequiredService<OllamaAiOptions>();

        Assert.Equal(OllamaFrozenProfile.Provider, options.Provider);
        Assert.Equal(OllamaFrozenProfile.BaseUrl, options.BaseUrl);
        Assert.Equal(OllamaFrozenProfile.Model, options.Model);
        Assert.Equal(OllamaFrozenProfile.TimeoutSeconds, options.TimeoutSeconds);
        Assert.Equal(OllamaFrozenProfile.Temperature, options.Temperature);
        Assert.Equal(OllamaFrozenProfile.ContextTokens, options.ContextTokens);
    }

    [Fact]
    public void The_registration_applies_the_frozen_profile_validator()
    {
        using var provider = BuildProvider();
        var validator = Assert.Single(provider.GetServices<IValidateOptions<OllamaAiOptions>>());

        var frozen = provider.GetRequiredService<OllamaAiOptions>();

        Assert.True(validator.Validate(null, frozen).Succeeded);

        var differentModel = new OllamaAiOptions
        {
            Provider = OllamaFrozenProfile.Provider,
            BaseUrl = OllamaFrozenProfile.BaseUrl,
            Model = "qwen3:1.7b",
            TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds,
            Temperature = OllamaFrozenProfile.Temperature,
            ContextTokens = OllamaFrozenProfile.ContextTokens,
        };

        var result = validator.Validate(null, differentModel);

        Assert.True(result.Failed);
        Assert.Contains(nameof(OllamaAiOptions.Model), string.Join("; ", result.Failures ?? []), StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntelligenceModule(options =>
        {
            options.Provider = OllamaFrozenProfile.Provider;
            options.BaseUrl = OllamaFrozenProfile.BaseUrl;
            options.Model = OllamaFrozenProfile.Model;
            options.TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds;
            options.Temperature = OllamaFrozenProfile.Temperature;
            options.ContextTokens = OllamaFrozenProfile.ContextTokens;
        });

        return services.BuildServiceProvider();
    }
}
