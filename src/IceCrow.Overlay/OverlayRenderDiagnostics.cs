namespace IceCrow.Overlay;

/// <summary>
/// Bounded developer counters for overlay rendering. These exist to answer
/// "is the overlay doing work it does not need to do?" and deliberately stay
/// counters rather than a rendering telemetry pipeline.
/// </summary>
public sealed class OverlayRenderDiagnostics
{
    private int _viewStateUpdatesApplied;
    private int _viewStateUpdatesSkipped;
    private int _opponentRowsReplaced;
    private int _animationsStarted;
    private int _imageCacheHits;
    private int _imageCacheMisses;
    private int _imageDecodes;

    /// <summary>View states that contained a visible change and reached WPF.</summary>
    public int ViewStateUpdatesApplied => Volatile.Read(ref _viewStateUpdatesApplied);

    /// <summary>View states that were value-equal to the previous one and were dropped.</summary>
    public int ViewStateUpdatesSkipped => Volatile.Read(ref _viewStateUpdatesSkipped);

    /// <summary>Individual lobby rows whose value changed and were replaced.</summary>
    public int OpponentRowsReplaced => Volatile.Read(ref _opponentRowsReplaced);

    /// <summary>Short finite animations started since process start.</summary>
    public int AnimationsStarted => Volatile.Read(ref _animationsStarted);

    public int ImageCacheHits => Volatile.Read(ref _imageCacheHits);

    public int ImageCacheMisses => Volatile.Read(ref _imageCacheMisses);

    /// <summary>Actual bitmap decodes. Repeated tile renders must not increase this.</summary>
    public int ImageDecodes => Volatile.Read(ref _imageDecodes);

    public override string ToString() =>
        $"view {ViewStateUpdatesApplied} applied / {ViewStateUpdatesSkipped} skipped · " +
        $"rows {OpponentRowsReplaced} · anim {AnimationsStarted} · " +
        $"img {ImageCacheHits} hit / {ImageCacheMisses} miss / {ImageDecodes} decoded";

    internal void RecordViewStateApplied() => Interlocked.Increment(ref _viewStateUpdatesApplied);

    internal void RecordViewStateSkipped() => Interlocked.Increment(ref _viewStateUpdatesSkipped);

    internal void RecordOpponentRowReplaced() => Interlocked.Increment(ref _opponentRowsReplaced);

    internal void RecordAnimationStarted() => Interlocked.Increment(ref _animationsStarted);

    internal void RecordImageCacheHit() => Interlocked.Increment(ref _imageCacheHits);

    internal void RecordImageCacheMiss() => Interlocked.Increment(ref _imageCacheMisses);

    internal void RecordImageDecode() => Interlocked.Increment(ref _imageDecodes);
}
