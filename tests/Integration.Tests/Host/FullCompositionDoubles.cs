using System.Collections.Concurrent;
using System.Globalization;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Host;

/// <summary>
/// The deterministic NLU seam of the full-composition tests. Each customer text maps to exactly one
/// scripted interpretation, and any text the test did not script fails the turn instead of guessing.
/// </summary>
internal sealed class ScriptedAiNluClient : IAiNluClient
{
    private readonly ConcurrentDictionary<string, NluAnalysisResult> script = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyList<string> Messages => [.. messages];

    public ScriptedAiNluClient With(string message, NluAnalysisResult analysis)
    {
        script[message] = analysis;

        return this;
    }

    public Task<NluAnalysisResult> AnalyzeAsync(
        string message,
        NluConversationContext context,
        CancellationToken cancellationToken)
    {
        messages.Enqueue(message);

        return script.TryGetValue(message, out var analysis)
            ? Task.FromResult(analysis)
            : throw new InvalidOperationException($"The test did not script an interpretation for '{message}'.");
    }
}

/// <summary>
/// The fake Meta outbound sender: it accepts every reply with a deterministic provider id and records
/// what the real Outbox worker asked it to send.
/// </summary>
internal sealed class RecordingOutboundSender : IOutboundMessageSender
{
    private readonly ConcurrentQueue<ClaimedOutboxMessage> sent = new();
    private int count;

    public IReadOnlyList<ClaimedOutboxMessage> Sent => [.. sent];

    public Task<OutboundSendResult> SendAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken = default)
    {
        sent.Enqueue(message);
        var number = Interlocked.Increment(ref count);

        return Task.FromResult(OutboundSendResult.Sent(string.Create(CultureInfo.InvariantCulture, $"wamid.out-{number}")));
    }
}
