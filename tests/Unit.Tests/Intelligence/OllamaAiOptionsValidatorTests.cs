using System.Text.Json;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

/// <summary>
/// The AI profile is the frozen Controlled Demo Candidate: the validator accepts exactly that profile
/// and names the offending setting for anything else, so the host can never start on an unmeasured
/// model configuration.
/// </summary>
public sealed class OllamaAiOptionsValidatorTests
{
    private readonly OllamaAiOptionsValidator _validator = new();

    [Fact]
    public void The_frozen_profile_is_accepted()
    {
        var result = _validator.Validate(null, Frozen());

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void The_provider_name_is_case_insensitive()
    {
        var options = Frozen();
        options.Provider = "ollama";

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("llama.cpp")]
    [InlineData("")]
    public void A_different_provider_is_rejected(string provider)
    {
        var options = Frozen();
        options.Provider = provider;

        AssertRejected(options, nameof(OllamaAiOptions.Provider));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/api/chat")]
    [InlineData("ftp://127.0.0.1:11434")]
    public void A_base_url_that_is_not_absolute_http_is_rejected(string baseUrl)
    {
        var options = Frozen();
        options.BaseUrl = baseUrl;

        AssertRejected(options, nameof(OllamaAiOptions.BaseUrl));
    }

    [Fact]
    public void An_https_base_url_is_accepted()
    {
        var options = Frozen();
        options.BaseUrl = "https://127.0.0.1:11434";

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("qwen3:1.7b")]
    [InlineData("qwen3.5:2b")]
    [InlineData("")]
    public void A_model_that_is_not_the_frozen_candidate_is_rejected(string model)
    {
        var options = Frozen();
        options.Model = model;

        AssertRejected(options, nameof(OllamaAiOptions.Model));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void A_timeout_other_than_the_frozen_value_is_rejected(int timeoutSeconds)
    {
        var options = Frozen();
        options.TimeoutSeconds = timeoutSeconds;

        AssertRejected(options, nameof(OllamaAiOptions.TimeoutSeconds));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    public void A_non_zero_temperature_is_rejected(double temperature)
    {
        var options = Frozen();
        options.Temperature = temperature;

        AssertRejected(options, nameof(OllamaAiOptions.Temperature));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8192)]
    public void A_context_size_other_than_the_frozen_value_is_rejected(int contextTokens)
    {
        var options = Frozen();
        options.ContextTokens = contextTokens;

        AssertRejected(options, nameof(OllamaAiOptions.ContextTokens));
    }

    [Fact]
    public void The_checked_in_host_template_is_the_frozen_profile()
    {
        var templatePath = Path.Combine(
            IntelligenceTestFiles.RepositoryRoot,
            "src",
            "Host.Web",
            "appsettings.json");

        using var document = JsonDocument.Parse(
            File.ReadAllText(templatePath),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        var ai = document.RootElement.GetProperty("Ai");

        var options = new OllamaAiOptions
        {
            Provider = ai.GetProperty(nameof(OllamaAiOptions.Provider)).GetString() ?? string.Empty,
            BaseUrl = ai.GetProperty(nameof(OllamaAiOptions.BaseUrl)).GetString() ?? string.Empty,
            Model = ai.GetProperty(nameof(OllamaAiOptions.Model)).GetString() ?? string.Empty,
            TimeoutSeconds = ai.GetProperty(nameof(OllamaAiOptions.TimeoutSeconds)).GetInt32(),
            Temperature = ai.GetProperty(nameof(OllamaAiOptions.Temperature)).GetDouble(),
            ContextTokens = ai.GetProperty(nameof(OllamaAiOptions.ContextTokens)).GetInt32(),
        };

        Assert.True(
            _validator.Validate(null, options).Succeeded,
            "The checked-in host template must be the frozen Controlled Demo Candidate profile.");
        Assert.Equal(OllamaAiOptions.ConfigurationSectionName, "Ai");
    }

    private static OllamaAiOptions Frozen() => new()
    {
        Provider = OllamaFrozenProfile.Provider,
        BaseUrl = OllamaFrozenProfile.BaseUrl,
        Model = OllamaFrozenProfile.Model,
        TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds,
        Temperature = OllamaFrozenProfile.Temperature,
        ContextTokens = OllamaFrozenProfile.ContextTokens,
    };

    private void AssertRejected(OllamaAiOptions options, string setting)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(setting, string.Join("; ", result.Failures ?? []), StringComparison.Ordinal);
    }
}
