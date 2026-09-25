using System.Net;
using System.Text;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

namespace WhatsAppMonitorAssistant.Unit.Tests.Intelligence;

public sealed class OllamaModelReadinessTests
{
    [Fact]
    public async Task Matching_name_is_ready()
    {
        var readiness = Create(
            HttpStatusCode.OK,
            """{"models":[{"name":"qwen3.5:2b-q4_K_M"}]}""");

        Assert.Equal(
            AiModelReadinessStatus.Ready,
            await readiness.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Matching_model_property_is_ready()
    {
        var readiness = Create(
            HttpStatusCode.OK,
            """{"models":[{"model":"qwen3.5:2b-q4_K_M"}]}""");

        Assert.Equal(
            AiModelReadinessStatus.Ready,
            await readiness.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Near_match_is_model_missing()
    {
        var readiness = Create(
            HttpStatusCode.OK,
            """{"models":[{"name":"qwen3.5:2b"}]}""");

        Assert.Equal(
            AiModelReadinessStatus.ModelMissing,
            await readiness.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Non_success_status_is_unavailable()
    {
        var readiness = Create(
            HttpStatusCode.InternalServerError,
            """{"models":[]}""");

        Assert.Equal(
            AiModelReadinessStatus.Unavailable,
            await readiness.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_json_is_unavailable()
    {
        var readiness = Create(
            HttpStatusCode.OK,
            "not-json");

        Assert.Equal(
            AiModelReadinessStatus.Unavailable,
            await readiness.CheckAsync(CancellationToken.None));
    }

    private static OllamaModelReadiness Create(HttpStatusCode status, string body)
    {
        var client = new HttpClient(new StaticHandler(status, body))
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
        };

        return new OllamaModelReadiness(
            new SingleClientFactory(client),
            new OllamaAiOptions
            {
                Provider = OllamaFrozenProfile.Provider,
                BaseUrl = OllamaFrozenProfile.BaseUrl,
                Model = OllamaFrozenProfile.Model,
                TimeoutSeconds = OllamaFrozenProfile.TimeoutSeconds,
                Temperature = OllamaFrozenProfile.Temperature,
                ContextTokens = OllamaFrozenProfile.ContextTokens,
            });
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            if (!string.Equals(name, OllamaModelReadiness.HttpClientName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected HttpClient name: {name}");
            }

            return client;
        }
    }

    private sealed class StaticHandler(
        HttpStatusCode status,
        string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
