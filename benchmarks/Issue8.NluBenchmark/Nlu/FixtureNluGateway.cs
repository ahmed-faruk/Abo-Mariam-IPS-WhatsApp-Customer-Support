namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Deterministic offline gateway used by unit tests and by <c>dry-run</c>. It never touches
/// the network and never reads the dataset expectations, so a dry-run exercises the harness
/// pipeline without producing benchmark evidence.
///
/// Scripted behaviour, by call order: the first reply is schema-invalid so the retry path
/// runs; the third and fourth replies stay invalid so one case fails after its retry; every
/// later reply is the minimal schema-valid object.
/// </summary>
public sealed class FixtureNluGateway : IOllamaGateway
{
    public const string MinimalValidOutput =
        """{"intent":"ProductSearch","requiredPorts":[],"grades":[],"budgetType":"None"}""";

    public const string InvalidOutput = """{"intent":"ProductSearch","budgetType":"None"}""";

    private int _callCount;

    public Task<OllamaHealth> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new OllamaHealth("offline-fixture", ["offline-fixture"]));

    public Task<NluTransportResponse> SendAsync(
        NluTransportRequest request,
        CancellationToken cancellationToken)
    {
        _callCount++;

        var content = _callCount switch
        {
            1 => InvalidOutput,
            2 => MinimalValidOutput,
            3 => InvalidOutput,
            4 => InvalidOutput,
            _ => MinimalValidOutput,
        };

        return Task.FromResult(new NluTransportResponse { Content = content });
    }
}
