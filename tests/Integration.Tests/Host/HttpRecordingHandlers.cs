using System.Net;
using System.Text;
using System.Text.Json;

namespace WhatsAppMonitorAssistant.Integration.Tests.Host;

internal sealed record RecordedHttpRequest(string Method, string Path);

internal sealed class StubOllamaTagsHandler : HttpMessageHandler
{
    private readonly string? responseBody;
    private readonly Exception? exception;
    private readonly List<RecordedHttpRequest> requests = [];

    private StubOllamaTagsHandler(string? responseBody, Exception? exception)
    {
        this.responseBody = responseBody;
        this.exception = exception;
    }

    public IReadOnlyList<RecordedHttpRequest> Requests => requests;

    public static StubOllamaTagsHandler WithNames(params string[] names) =>
        new(
            JsonSerializer.Serialize(new
            {
                models = names.Select(name => new { name }).ToArray(),
            }),
            null);

    public static StubOllamaTagsHandler Throw(Exception exception) =>
        new(null, exception);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty));

        if (exception is not null)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseBody ?? """{"models":[]}""",
                Encoding.UTF8,
                "application/json"),
        });
    }
}

internal sealed class RecordingMetaHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedHttpRequest> requests = [];

    public IReadOnlyList<RecordedHttpRequest> Requests => requests;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"messages":[{"id":"wamid.test"}]}""",
                Encoding.UTF8,
                "application/json"),
        });
    }
}

internal sealed class RecordingInferenceHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedHttpRequest> requests = [];

    public IReadOnlyList<RecordedHttpRequest> Requests => requests;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
