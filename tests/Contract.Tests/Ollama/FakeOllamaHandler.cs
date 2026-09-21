using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Contract.Tests.Ollama;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/>: it records every request and answers with the next
/// scripted step, so the contract suite exercises the real adapter and the real HTTP object graph with
/// no Ollama process anywhere.
/// </summary>
internal sealed class FakeOllamaHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _steps = new();

    public List<CapturedOllamaRequest> Requests { get; } = [];

    public int Attempts => Requests.Count;

    /// <summary>Completes as soon as the first request has been recorded and is about to be answered.</summary>
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeOllamaHandler ThenJson(string modelContent) =>
        ThenRaw(ChatEnvelope(modelContent));

    public FakeOllamaHandler ThenRaw(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Then(_ => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public FakeOllamaHandler ThenStatus(HttpStatusCode statusCode) =>
        ThenRaw(ChatEnvelope("ignored"), statusCode);

    public FakeOllamaHandler ThenFailure(Exception exception) =>
        Then(_ => Task.FromException<HttpResponseMessage>(exception));

    public FakeOllamaHandler ThenResponse(Func<CancellationToken, HttpResponseMessage> response) =>
        Then(cancellationToken => Task.FromResult(response(cancellationToken)));

    public FakeOllamaHandler ThenHang() =>
        Then(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The hanging step must be cancelled.");
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedOllamaRequest(request.Method, request.RequestUri, body));
        RequestStarted.TrySetResult();

        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("The scripted Ollama handler ran out of steps.");
        }

        return await _steps.Dequeue()(cancellationToken);
    }

    private FakeOllamaHandler Then(Func<CancellationToken, Task<HttpResponseMessage>> step)
    {
        _steps.Enqueue(step);

        return this;
    }

    public static string ChatEnvelope(string modelContent) => new JsonObject
    {
        ["model"] = "qwen3.5:2b-q4_K_M",
        ["message"] = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = modelContent,
        },
        ["done"] = true,
    }.ToJsonString();
}

/// <summary>One recorded HTTP attempt, with the body the adapter actually sent.</summary>
internal sealed record CapturedOllamaRequest(HttpMethod Method, Uri? Uri, string? Body);
