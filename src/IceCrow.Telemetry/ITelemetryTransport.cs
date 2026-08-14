namespace IceCrow.Telemetry;

public sealed record TelemetryUploadResult(IReadOnlyList<Guid> AcknowledgedMatchIds);

public interface ITelemetryTransport
{
    Task<TelemetryUploadResult> UploadAsync(
        IReadOnlyList<MatchSummary> summaries,
        CancellationToken cancellationToken = default);
}
