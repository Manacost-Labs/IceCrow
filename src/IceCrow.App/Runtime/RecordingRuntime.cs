using System.IO;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;

namespace IceCrow.App.Runtime;

/// <summary>
/// Optional developer match capture. Composes the live applied-event stream
/// into private recordings: capture is off by default, saves go through the
/// bounded atomic capture store, and every failure is reported as status
/// instead of ever reaching live tracking. An in-flight capture is discarded
/// on disable or shutdown rather than saved as a complete match.
/// </summary>
internal sealed class RecordingRuntime : IAppliedMatchEventObserver, IAsyncDisposable
{
    private const int EventCountPublishStride = 50;

    private readonly object _gate = new();
    private readonly PrivateCaptureStore _store;
    private readonly MatchCaptureSession _session = new();
    private readonly Action<RecordingCaptureStatus> _onStatusChanged;
    private Task _pendingSave = Task.CompletedTask;
    private RecordingCapturePhase _phase = RecordingCapturePhase.Off;
    private bool _enabled;
    private bool _cleanedUp;
    private int _savedCaptureCount;
    private string? _lastSavedFileName;
    private string? _lastError;

    public RecordingRuntime(
        string localDataDirectory,
        Action<RecordingCaptureStatus> onStatusChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(onStatusChanged);
        _store = new PrivateCaptureStore(
            Path.Combine(localDataDirectory, "private-captures"));
        _onStatusChanged = onStatusChanged;
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            if (enabled)
            {
                _phase = RecordingCapturePhase.Waiting;
            }
            else
            {
                _session.Discard("Capture was disabled by the developer.");
                _phase = RecordingCapturePhase.Off;
            }
        }

        Publish();
    }

    public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
    {
        try
        {
            lock (_gate)
            {
                if (!_enabled)
                {
                    return;
                }

                _session.OnMatchStarted(timestamp, localPlayerId);
                _phase = RecordingCapturePhase.Recording;
                _lastError = null;
            }

            Publish();
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    public void OnEventApplied(GameEvent gameEvent)
    {
        try
        {
            bool publish;
            lock (_gate)
            {
                if (!_session.IsCapturing)
                {
                    return;
                }

                _session.OnEventApplied(gameEvent);
                if (!_session.IsCapturing)
                {
                    _phase = RecordingCapturePhase.Failed;
                    _lastError = "Capture was discarded mid-match; see match end for the reason.";
                    publish = true;
                }
                else
                {
                    publish = _session.CurrentEventCount % EventCountPublishStride == 0;
                }
            }

            if (publish)
            {
                Publish();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    public void OnEventRejected(GameEvent gameEvent)
    {
        try
        {
            var publish = false;
            lock (_gate)
            {
                if (_session.IsCapturing)
                {
                    _session.OnEventRejected(gameEvent);
                    _phase = RecordingCapturePhase.Failed;
                    _lastError = "A tracking safety rejection made the capture incomplete.";
                    publish = true;
                }
            }

            if (publish)
            {
                Publish();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    public void OnMatchEnded(DateTimeOffset timestamp)
    {
        try
        {
            MatchCaptureCompletion? completion;
            lock (_gate)
            {
                completion = _session.OnMatchEnded(timestamp);
                if (completion is { Match: not null })
                {
                    _pendingSave = SaveAfterAsync(_pendingSave, completion.Match);
                }
                else if (completion is { DiscardReason: { } reason })
                {
                    _phase = RecordingCapturePhase.Failed;
                    _lastError = reason;
                }
            }

            if (completion is not null)
            {
                Publish();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pendingSave;
        lock (_gate)
        {
            _session.Discard("IceCrow shut down while the match was still running.");
            pendingSave = _pendingSave;
        }

        // The save chain reports its own failures and never faults.
        await pendingSave.ConfigureAwait(false);
    }

    private async Task SaveAfterAsync(Task previous, RecordedMatch match)
    {
        await previous.ConfigureAwait(false);
        try
        {
            if (!_cleanedUp)
            {
                _cleanedUp = true;
                _ = _store.CleanupAbandonedTemporaryFiles();
            }

            var result = await _store.SaveAsync(match).ConfigureAwait(false);
            lock (_gate)
            {
                _savedCaptureCount++;
                _lastSavedFileName = Path.GetFileName(result.CapturePath);
                _lastError = null;
                _phase = _enabled ? RecordingCapturePhase.Saved : RecordingCapturePhase.Off;
            }

            Publish();
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    private void ReportFailure(string message)
    {
        lock (_gate)
        {
            _lastError = message;
            _phase = _enabled ? RecordingCapturePhase.Failed : RecordingCapturePhase.Off;
        }

        Publish();
    }

    private void Publish()
    {
        RecordingCaptureStatus status;
        lock (_gate)
        {
            status = new RecordingCaptureStatus(
                _phase,
                _enabled,
                _session.CurrentEventCount,
                _savedCaptureCount,
                _lastSavedFileName,
                _lastError);
        }

        _onStatusChanged(status);
    }
}
