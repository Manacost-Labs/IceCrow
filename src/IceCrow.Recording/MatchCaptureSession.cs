using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording;

/// <summary>
/// Turns one authoritative applied-event stream into at most one
/// <see cref="RecordedMatch"/> per match, without guessing lifecycle itself.
/// The session never overstates completeness: a tracking safety rejection or a
/// recorder limit permanently discards the current capture, and the discard
/// reason is reported at match end instead of a match. Not thread-safe; the
/// caller serializes notifications the same way the live coordinator does.
/// </summary>
public sealed class MatchCaptureSession
{
    private MatchRecorder? _recorder;
    private string? _discardReason;
    private bool _matchActive;

    /// <summary>True while a match is active and its capture is still intact.</summary>
    public bool IsCapturing => _recorder is not null;

    public int CurrentEventCount => _recorder?.EventCount ?? 0;

    public long CurrentRetainedBytes => _recorder?.RetainedBytes ?? 0;

    public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
    {
        _matchActive = true;
        _discardReason = null;
        _recorder = new MatchRecorder(timestamp);
        _recorder.RecordMatchStarted(timestamp, localPlayerId);
    }

    public void OnEventApplied(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        if (_recorder is not { } recorder)
        {
            return;
        }

        try
        {
            recorder.Record(gameEvent);
        }
        catch (InvalidOperationException exception)
        {
            Discard($"Recorder limit reached: {exception.Message}");
        }
    }

    public void OnEventRejected(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        if (_recorder is not null)
        {
            Discard(
                "A tracking safety limit rejected an event, so the capture " +
                "would be incomplete evidence of the match.");
        }
    }

    /// <summary>
    /// Completes the current capture. Returns null when no match was being
    /// observed; otherwise either the finished match or the discard reason.
    /// </summary>
    public MatchCaptureCompletion? OnMatchEnded(DateTimeOffset timestamp)
    {
        if (!_matchActive)
        {
            return null;
        }

        _matchActive = false;
        if (_recorder is not { } recorder)
        {
            var reason = _discardReason ?? "The capture was discarded.";
            _discardReason = null;
            return new MatchCaptureCompletion(null, reason);
        }

        _recorder = null;
        try
        {
            recorder.RecordMatchEnded(timestamp);
            return new MatchCaptureCompletion(recorder.CreateMatch(), null);
        }
        catch (InvalidOperationException exception)
        {
            return new MatchCaptureCompletion(
                null,
                $"Recorder limit reached: {exception.Message}");
        }
    }

    /// <summary>Drops any in-flight capture, e.g. on shutdown or disable.</summary>
    public void Discard(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (_recorder is not null || _matchActive)
        {
            _discardReason = reason;
        }

        _recorder = null;
    }
}

/// <summary>
/// Outcome of one observed match: exactly one of <see cref="Match"/> or
/// <see cref="DiscardReason"/> is set.
/// </summary>
public sealed record MatchCaptureCompletion(
    RecordedMatch? Match,
    string? DiscardReason);
