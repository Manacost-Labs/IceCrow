namespace IceCrow.Telemetry;

public sealed class TelemetryUploader
{
    private readonly TelemetryConsent _consent;
    private readonly TelemetryOutbox _outbox;
    private readonly ITelemetryTransport _transport;

    public TelemetryUploader(
        TelemetryConsent consent,
        TelemetryOutbox outbox,
        ITelemetryTransport transport)
    {
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(transport);
        _consent = consent;
        _outbox = outbox;
        _transport = transport;
    }

    public DateTimeOffset? LastUpload { get; private set; }

    public async Task<bool> UploadOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_consent.IsEnabled)
        {
            return false;
        }

        var batch = await _outbox.PeekBatchAsync(25, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return true;
        }

        var result = await _transport.UploadAsync(batch, cancellationToken).ConfigureAwait(false);
        var submitted = batch.Select(item => item.MatchId).ToHashSet();
        var acknowledged = result.AcknowledgedMatchIds
            .Where(submitted.Contains)
            .Distinct()
            .Take(batch.Count)
            .ToArray();
        await _outbox.AcknowledgeAsync(acknowledged, cancellationToken).ConfigureAwait(false);
        LastUpload = DateTimeOffset.UtcNow;
        return acknowledged.Length == batch.Count;
    }
}
