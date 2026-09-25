using System.Net;
using System.Text.Json;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Host;

[Collection(PostgresCollection.Name)]
public sealed class ReadinessEndpointTests(PostgresContainerFixture postgres)
{
    private const string FrozenModel = "qwen3.5:2b-q4_K_M";
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=readiness_unreachable;Username=monitor_app";

    [Fact]
    public async Task Database_up_and_frozen_model_present_returns_200()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var ollama = StubOllamaTagsHandler.WithNames(FrozenModel);

        await using var factory = new ApplicationHostFactory(connectionString, ollama);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        AssertStatus(response, HttpStatusCode.OK, "READINESS_STATUS");
        await AssertReadinessBodyAsync(response, "Healthy", "Healthy", "Healthy");
        AssertDependencyCalls(factory, ollama, 1);
    }

    [Fact]
    public async Task Database_down_returns_503()
    {
        var ollama = StubOllamaTagsHandler.WithNames(FrozenModel);

        await using var factory = new ApplicationHostFactory(UnreachableConnectionString, ollama);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        AssertStatus(response, HttpStatusCode.ServiceUnavailable, "READINESS_STATUS");
        await AssertReadinessBodyAsync(response, "Unhealthy", "Unhealthy", "Healthy");
        AssertDependencyCalls(factory, ollama, 1);
    }

    [Fact]
    public async Task Missing_frozen_model_returns_503()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var ollama = StubOllamaTagsHandler.WithNames("qwen3.5:2b", "llama3:8b");

        await using var factory = new ApplicationHostFactory(connectionString, ollama);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        AssertStatus(response, HttpStatusCode.ServiceUnavailable, "READINESS_STATUS");
        await AssertReadinessBodyAsync(response, "Unhealthy", "Healthy", "Unhealthy");
        AssertDependencyCalls(factory, ollama, 1);
    }

    [Fact]
    public async Task Ollama_transport_failure_returns_503()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var ollama = StubOllamaTagsHandler.Throw(new HttpRequestException("test transport failure"));

        await using var factory = new ApplicationHostFactory(connectionString, ollama);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        AssertStatus(response, HttpStatusCode.ServiceUnavailable, "READINESS_STATUS");
        await AssertReadinessBodyAsync(response, "Unhealthy", "Healthy", "Unhealthy");
        AssertDependencyCalls(factory, ollama, 1);
    }

    [Fact]
    public async Task Liveness_stays_200_when_database_is_down()
    {
        var ollama = StubOllamaTagsHandler.WithNames(FrozenModel);

        await using var factory = new ApplicationHostFactory(UnreachableConnectionString, ollama);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        AssertStatus(response, HttpStatusCode.OK, "LIVENESS_STATUS");
        AssertDependencyCalls(factory, ollama, 0);
    }

    private static void AssertStatus(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string marker) =>
        Assert.True(
            response.StatusCode == expected,
            $"{marker} expected={(int)expected} actual={(int)response.StatusCode}");

    private static async Task AssertReadinessBodyAsync(
        HttpResponseMessage response,
        string overall,
        string postgres,
        string ollama)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(overall, root.GetProperty("status").GetString());

        var checks = root.GetProperty("checks");

        Assert.Equal(postgres, checks.GetProperty("postgresql").GetString());
        Assert.Equal(ollama, checks.GetProperty("ollama-model").GetString());
    }

    private static void AssertDependencyCalls(
        ApplicationHostFactory factory,
        StubOllamaTagsHandler ollama,
        int expectedOllamaCalls)
    {
        Assert.Equal(expectedOllamaCalls, ollama.Requests.Count);

        Assert.All(ollama.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Equal("/api/tags", request.Path);
        });

        Assert.Empty(factory.InferenceHandler.Requests);
        Assert.Empty(factory.MetaHandler.Requests);
    }
}
