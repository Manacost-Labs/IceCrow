using System.IO;
using System.Threading.Channels;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;

namespace IceCrow.App.Runtime;

/// <summary>
/// Optional developer match capture. Converts the live applied-event stream
/// into private recordings while keeping two truths separate: the capture
/// session (Off/Waiting/Recording) and completed-capture persistence
/// (Idle/Saving/Saved/Warning/Failed). Completed matches leave the observer
/// callback through a bounded queue and are saved by one sequential worker;
/// an intentional developer action is reported as a notice, never as a
/// failure, and shutdown drains pending saves for a bounded grace period
/// before cancelling them. Every failure surfaces as status only — live
/// tracking is never affected.
/// </summary>
internal sealed class RecordingRuntime : IAppliedMatchEventObserver, IAsyncDisposable
{
    // A save normally finishes long before the next Battlegrounds match ends,
    // so two pending completed matches cover even a pathologically slow disk.
    public const int PendingSaveCapacity = 2;
    public static readonly TimeSpan DefaultShutdownGracePeriod = TimeSpan.FromSeconds(5);

    private const int EventCountPublishStride = 50;

    private readonly object _gate = new();
    private readonly MatchCaptureSession _session = new();
    private readonly Channel<RecordedMatch> _pendingMatches;
    private readonly Func<RecordedMatch, CancellationToken, Task<PrivateCaptureSaveResult>> _persistCapture;
    private readonly Action<RecordingCaptureStatus> _onStatusChanged;
    private readonly TimeSpan _shutdownGracePeriod;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PrivateCaptureStore? _store;
    private Task _worker = Task.CompletedTask;
    private bool _workerStarted;
    private bool _cleanedUp;
    private bool _enabled;
    private bool _intentionalDiscard;
    private bool _disposed;
    private RecordingSessionPhase _sessionPhase = RecordingSessionPhase.Off;
    private RecordingPersistencePhase _persistencePhase = RecordingPersistencePhase.Idle;
    private int _pendingSaveCount;
    private int _savedCaptureCount;
    private string? _lastSavedFileName;
    private string? _lastNotice;
    private string? _lastWarning;
    private string? _lastError;

    public RecordingRuntime(
        string localDataDirectory,
        Action<RecordingCaptureStatus> onStatusChanged,
        Func<RecordedMatch, CancellationToken, Task<PrivateCaptureSaveResult>>? persistCapture = null,
        TimeSpan? shutdownGracePeriod = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(onStatusChanged);
        _onStatusChanged = onStatusChanged;
        if (persistCapture is null)
        {
            _store = new PrivateCaptureStore(
                Path.Combine(localDataDirectory, "private-captures"));
            _persistCapture = PersistToStoreAsync;
        }
        else
        {
            _persistCapture = persistCapture;
        }

        _shutdownGracePeriod = shutdownGracePeriod ?? DefaultShutdownGracePeriod;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pendingMatches = Channel.CreateBounded<RecordedMatch>(
            new BoundedChannelOptions(PendingSaveCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>Starts the persistence worker. Called once by the composition root.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_workerStarted)
            {
                throw new InvalidOperationException(
                    "The recording runtime can only be started once.");
            }

            _workerStarted = true;
        }

        _worker = RunWorkerAsync();
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
                _sessionPhase = RecordingSessionPhase.WaitingForNextMatch;
            }
            else
            {
                if (_session.IsCapturing)
                {
                    _session.Discard(
                        "Capture was disabled by the developer; the current match was discarded.");
                    _intentionalDiscard = true;
                    _lastNotice = "Capture disabled; the current match capture was discarded.";
                }

                _sessionPhase = RecordingSessionPhase.Off;
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
                _sessionPhase = RecordingSessionPhase.Recording;
                _intentionalDiscard = false;
            }

            Publish();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
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
                    // The session discarded itself on a recorder limit.
                    _lastError = "Capture discarded mid-match; the reason is reported at match end.";
                    _sessionPhase = ActiveIdlePhase();
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
            ReportFailure(exception);
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
                    _lastError = "A tracking safety rejection made the capture incomplete; it was discarded.";
                    _sessionPhase = ActiveIdlePhase();
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
            ReportFailure(exception);
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
                switch (completion)
                {
                    case null:
                        return;
                    case { Match: { } match }:
                        _sessionPhase = ActiveIdlePhase();
                        if (_pendingMatches.Writer.TryWrite(match))
                        {
                            _pendingSaveCount++;
                            _persistencePhase = RecordingPersistencePhase.Saving;
                        }
                        else
                        {
                            _lastError = "Completed capture dropped: the persistence queue is full.";
                            _persistencePhase = RecordingPersistencePhase.Failed;
                        }

                        break;
                    case { DiscardReason: { } reason }:
                        if (_intentionalDiscard)
                        {
                            _lastNotice = reason;
                        }
                        else
                        {
                            _lastError = reason;
                            _sessionPhase = ActiveIdlePhase();
                        }

                        _intentionalDiscard = false;
                        break;
                }
            }

            Publish();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_session.IsCapturing)
            {
                _session.Discard("IceCrow shut down while the match was still running.");
                _intentionalDiscard = true;
                _lastNotice = "Shutdown discarded the in-flight match capture.";
                _sessionPhase = RecordingSessionPhase.Off;
            }
        }

        _pendingMatches.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(_shutdownGracePeriod, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _shutdown.Cancel();
            try
            {
                await _worker.WaitAsync(_shutdownGracePeriod, _timeProvider).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The save ignored cancellation. Abandon it rather than block
                // shutdown; observe its eventual fault so it can never surface
                // as an unobserved task exception. The token source stays
                // undisposed because the abandoned worker may still read it.
                _ = _worker.ContinueWith(
                    static task => _ = task.Exception,
                    TaskScheduler.Default);
                return;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            await foreach (var match in _pendingMatches.Reader
                               .ReadAllAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                await PersistAsync(match).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            ReportAbandonedQueue();
        }
    }

    private async Task PersistAsync(RecordedMatch match)
    {
        lock (_gate)
        {
            _persistencePhase = RecordingPersistencePhase.Saving;
        }

        Publish();
        try
        {
            var result = await _persistCapture(match, _shutdown.Token).ConfigureAwait(false);
            lock (_gate)
            {
                _pendingSaveCount--;
                _savedCaptureCount++;
                _lastSavedFileName = Path.GetFileName(result.CapturePath);
                if (result.MaintenanceWarning is { } warning)
                {
                    _lastWarning = warning;
                    _persistencePhase = RecordingPersistencePhase.Warning;
                }
                else
                {
                    _persistencePhase = RecordingPersistencePhase.Saved;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            lock (_gate)
            {
                _pendingSaveCount--;
                _lastWarning = "A pending capture save was cancelled during shutdown.";
                _persistencePhase = RecordingPersistencePhase.Warning;
            }

            Publish();
            throw;
        }
        catch (Exception exception)
        {
            // Isolation boundary for optional persistence: any storage failure
            // becomes status. The message stays in the local Debug UI only.
            lock (_gate)
            {
                _pendingSaveCount--;
                _lastError = $"Capture save failed: {exception.Message}";
                _persistencePhase = RecordingPersistencePhase.Failed;
            }
        }

        Publish();
    }

    private async Task<PrivateCaptureSaveResult> PersistToStoreAsync(
        RecordedMatch match,
        CancellationToken cancellationToken)
    {
        if (!_cleanedUp)
        {
            _cleanedUp = true;
            try
            {
                _ = _store!.CleanupAbandonedTemporaryFiles();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lock (_gate)
                {
                    _lastWarning = $"Temporary-file cleanup failed: {exception.Message}";
                }
            }
        }

        return await _store!.SaveAsync(match, cancellationToken).ConfigureAwait(false);
    }

    private void ReportAbandonedQueue()
    {
        var abandoned = 0;
        while (_pendingMatches.Reader.TryRead(out _))
        {
            abandoned++;
        }

        if (abandoned == 0)
        {
            return;
        }

        lock (_gate)
        {
            _pendingSaveCount -= abandoned;
            _lastWarning =
                $"{abandoned} completed capture(s) were not persisted before shutdown.";
            _persistencePhase = RecordingPersistencePhase.Warning;
        }

        Publish();
    }

    private void ReportFailure(Exception exception)
    {
        lock (_gate)
        {
            _lastError = exception.Message;
        }

        Publish();
    }

    private RecordingSessionPhase ActiveIdlePhase() => _enabled
        ? RecordingSessionPhase.WaitingForNextMatch
        : RecordingSessionPhase.Off;

    private void Publish()
    {
        RecordingCaptureStatus status;
        lock (_gate)
        {
            status = new RecordingCaptureStatus(
                _enabled,
                _sessionPhase,
                _session.CurrentEventCount,
                _persistencePhase,
                _pendingSaveCount,
                _savedCaptureCount,
                _lastSavedFileName,
                _lastNotice,
                _lastWarning,
                _lastError);
        }

        try
        {
            _onStatusChanged(status);
        }
        catch
        {
            // Status is advisory; a throwing consumer must never affect capture.
        }
    }
}
