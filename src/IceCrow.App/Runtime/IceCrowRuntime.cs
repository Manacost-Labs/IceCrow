using System.Windows.Threading;
using IceCrow.Hearthstone.Logs;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Live;
using IceCrow.ProfileSync;

namespace IceCrow.App.Runtime;

internal sealed class IceCrowRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DataRuntime _data;
    private readonly TelemetryRuntime _telemetry;
    private readonly IOverlayPresentation? _presentation;
    private readonly ProfileSyncRuntime? _profileSync;
    private readonly RecordingRuntime? _recording;
    private readonly LiveRuntime _live;
    private readonly ProfileRecordPipeline? _profileRecords;
    private readonly Action<GameSessionUpdate> _onSessionProcessed;
    private Task[] _backgroundTasks = [];
    private int _started;
    private int _stopRequested;
    private int _stopped;

    public IceCrowRuntime(
        string localDataDirectory,
        Dispatcher dispatcher,
        IceCrowRuntimeOptions options,
        Action<GameSessionUpdate> onSessionProcessed,
        Action<ManacostDataStatus> onDataStatusChanged,
        Action<bool, int, DateTimeOffset?> onTelemetryStatusChanged,
        Action<ProfileSyncStatus> onProfileSyncStatusChanged,
        Action<RecordingCaptureStatus> onCaptureStatusChanged,
        Action<Exception> onRecoverableLogError,
        Action<string> onLogStatus,
        string clientVersion)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onSessionProcessed);
        Options = options;
        _onSessionProcessed = onSessionProcessed;
        _data = new DataRuntime(localDataDirectory, onDataStatusChanged);
        _telemetry = new TelemetryRuntime(localDataDirectory, clientVersion, onTelemetryStatusChanged);
        // The overlay is an optional feature. Headless composition never calls
        // into OverlayComposition, so the overlay and presentation assemblies
        // are never loaded and no WPF dispatch happens per tracking snapshot.
        _presentation = options.OverlayEnabled
            ? OverlayComposition.Create(dispatcher, _data.Database)
            : null;
        _profileSync = options.ProfileSyncEnabled
            ? new ProfileSyncRuntime(localDataDirectory, options.HearthPulseOrigin, onProfileSyncStatusChanged, clientVersion)
            : null;
        _profileRecords = _profileSync is { } profileSync
            ? new ProfileRecordPipeline(profileSync.TryQueue, profileSync.SetGameplayActive)
            : null;
        // Developer match capture is a Debug-only feature. Release composes a
        // null observer so the live hot path pays exactly one null check per
        // notification point and no capture lock or interface call per event.
#if DEBUG
        _recording = new RecordingRuntime(localDataDirectory, onCaptureStatusChanged);
#else
        _recording = null;
        _ = onCaptureStatusChanged;
#endif
        _live = new LiveRuntime(
            OnSessionProcessed,
            onRecoverableLogError,
            onLogStatus,
            _recording);
    }

    public IceCrowRuntimeOptions Options { get; }

    /// <summary>True when the overlay presentation was composed for this process.</summary>
    public bool OverlayComposed => _presentation is not null;

    public bool ProfileSyncComposed => _profileSync is not null;

    /// <summary>Overlay render counters when the overlay is enabled; null in headless mode.</summary>
    public object? OverlayDiagnostics => _presentation?.Diagnostics;

    public ProfileSyncRuntime? ProfileSync => _profileSync;

    /// <summary>Profile records produced from finished matches; null when profile sync is disabled.</summary>
    public ProfileRecordPipeline? ProfileRecords => _profileRecords;

    public PowerLogTailerDiagnostics TailerDiagnostics =>
        _live.TailerDiagnostics;

    public void SetCaptureEnabled(bool enabled) => _recording?.SetEnabled(enabled);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The IceCrow runtime can only be started once.");
        }

        _presentation?.Start();
        _recording?.Start();
        _backgroundTasks =
        [
            _data.RunAsync(_shutdown.Token),
            _telemetry.RunAsync(_shutdown.Token),
            _profileSync?.RunAsync(_shutdown.Token) ?? Task.CompletedTask,
            _live.RunAsync(_shutdown.Token),
        ];
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        RequestStop();
        try
        {
            await Task.WhenAll(_backgroundTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            await _live.DisposeAsync().ConfigureAwait(false);
            if (_recording is not null)
            {
                await _recording.DisposeAsync().ConfigureAwait(false);
            }

            await _telemetry.DisposeAsync().ConfigureAwait(false);
            if (_profileSync is not null)
            {
                await _profileSync.DisposeAsync().ConfigureAwait(false);
            }

            await _data.DisposeAsync().ConfigureAwait(false);
            if (_presentation is not null)
            {
                await _presentation.DisposeAsync().ConfigureAwait(false);
            }

            _shutdown.Dispose();
        }
    }

    public void PrepareForSynchronousExit()
    {
        // Called only from WPF's dispatcher-bound OnExit fallback. Releasing the
        // UI owner here prevents a later background continuation from waiting on
        // the dispatcher while OnExit synchronously waits for background tasks.
        _presentation?.DisposeSynchronouslyForExit();
        RequestStop();
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        // Stop accepting work, then cancel every producer and consumer through
        // the single process-lifetime token.
        _telemetry.Complete();
        _profileSync?.Complete();
        _shutdown.Cancel();
    }

    private void OnSessionProcessed(GameSessionUpdate update)
    {
        _onSessionProcessed(update);
        _profileRecords?.Observe(update, _live.GameplayActive);
        if (update.Battlegrounds is not { StateChanged: true, Snapshot: { } snapshot })
        {
            return;
        }

        _presentation?.Publish(snapshot);
        _telemetry.TryQueue(snapshot);
    }
}
