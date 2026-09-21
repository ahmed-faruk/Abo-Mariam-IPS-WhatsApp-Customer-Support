using System.Text.Json.Nodes;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Infrastructure.Ollama;

/// <summary>
/// The one call the adapter makes to Ollama: one body to <c>/api/chat</c>. It is a seam because the
/// retry decision depends on whether a model reply arrived at all, and that decision is unit-tested
/// without HTTP.
/// </summary>
public interface IOllamaChatTransport
{
    /// <summary>
    /// Sends one chat request. A caller cancellation is propagated as an
    /// <see cref="OperationCanceledException"/>; a configured timeout and an unreachable runtime are
    /// returned as outcomes, because only a received model reply may be corrected and retried.
    /// </summary>
    Task<OllamaChatTransportResult> SendAsync(JsonObject request, CancellationToken cancellationToken);
}
