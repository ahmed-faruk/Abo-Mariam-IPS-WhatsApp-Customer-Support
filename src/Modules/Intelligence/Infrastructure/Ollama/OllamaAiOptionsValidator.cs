using Microsoft.Extensions.Options;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// Rejects any AI configuration that is not the frozen Controlled Demo Candidate profile of
/// docs/TECHNICAL.md section 8.2. The profile was measured, so a different model, endpoint, timeout,
/// temperature or context size is a new documented decision and never a silent deployment default.
/// </summary>
public sealed class OllamaAiOptionsValidator : IValidateOptions<OllamaAiOptions>
{
    public ValidateOptionsResult Validate(string? name, OllamaAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!string.Equals(options.Provider, OllamaFrozenProfile.Provider, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.Provider)}' must be "
                + $"'{OllamaFrozenProfile.Provider}' for the Controlled Demo Candidate of "
                + $"docs/TECHNICAL.md section 8.2, but was '{options.Provider}'.");
        }

        if (!IsAbsoluteHttpUrl(options.BaseUrl))
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.BaseUrl)}' must be an absolute http or https "
                + $"URL such as '{OllamaFrozenProfile.BaseUrl}', but was '{options.BaseUrl}'.");
        }

        if (!string.Equals(options.Model, OllamaFrozenProfile.Model, StringComparison.Ordinal))
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.Model)}' must be the frozen Controlled Demo "
                + $"Candidate '{OllamaFrozenProfile.Model}', but was '{options.Model}'. A different model "
                + "is a new benchmark decision, not a configuration value.");
        }

        if (options.TimeoutSeconds != OllamaFrozenProfile.TimeoutSeconds)
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.TimeoutSeconds)}' must be "
                + $"{OllamaFrozenProfile.TimeoutSeconds}, matching docs/TECHNICAL.md section 8.2, but was "
                + $"{options.TimeoutSeconds}.");
        }

        if (options.Temperature != OllamaFrozenProfile.Temperature)
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.Temperature)}' must be "
                + $"{OllamaFrozenProfile.Temperature} for deterministic structured extraction, but was "
                + $"{options.Temperature}.");
        }

        if (options.ContextTokens != OllamaFrozenProfile.ContextTokens)
        {
            failures.Add(
                $"The AI setting '{nameof(OllamaAiOptions.ContextTokens)}' must be "
                + $"{OllamaFrozenProfile.ContextTokens}, matching docs/TECHNICAL.md section 8.2, but was "
                + $"{options.ContextTokens}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpUrl(string candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
