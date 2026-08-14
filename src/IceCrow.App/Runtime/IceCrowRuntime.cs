using System.Windows.Threading;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Live;

namespace IceCrow.App.Runtime;

internal sealed class IceCrowRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DataRuntime _data;
    private readonly TelemetryRuntime _telemetry;
    private readonly PresentationRuntime _presentation;
    private readonly LiveRuntime _live;
    private readonly Action<LiveTrackingUpdate> _onLiveTrackingProcessed;
    private Task[] _backgroundTasks = [];
    private int _started;
    private int _stopRequested;
    private int _stopped;

    public IceCrowRuntime(
        string localDataDirectory,
        Dispatcher dispatcher,
        Action<LiveTrackingUpdate> onLiveTrackingProcessed,
        Action<ManacostDataStatus> onDataStatusChanged,
        Action<bool, int, DateTimeOffset?> onTelemetryStatusChanged,
        Action<Exception> onRecoverableLogError,
        Action<string> onLogStatus,
        string clientVersion)
    {
        ArgumentNullException.ThrowIfNull(onLiveTrackingProcessed);
        _onLiveTrackingProcessed = onLiveTrackingProcessed;
        _data = new DataRuntime(localDataDirectory, onDataStatusChanged);
        _telemetry = new TelemetryRuntime(localDataDirectory, clientVersion, onTelemetryStatusChanged);
        _presentation = new PresentationRuntime(dispatcher, _data.Database);
        _live = new LiveRuntime(OnLiveTrackingProcessed, onRecoverableLogError, onLogStatus);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The IceCrow runtime can only be started once.");
        }

        _presentation.Start();
        _backgroundTasks =
        [
            _data.RunAsync(_shutdown.Token),
            _telemetry.RunAsync(_shutdown.Token),
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
            await _telemetry.DisposeAsync().ConfigureAwait(false);
            await _data.DisposeAsync().ConfigureAwait(false);
            await _presentation.DisposeAsync().ConfigureAwait(false);
            _shutdown.Dispose();
        }
    }

    public void PrepareForSynchronousExit()
    {
        // Called only from WPF's dispatcher-bound OnExit fallback. Releasing the
        // UI owner here prevents a later background continuation from waiting on
        // the dispatcher while OnExit synchronously waits for background tasks.
        _presentation.DisposeSynchronouslyForExit();
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
        _shutdown.Cancel();
    }

    private void OnLiveTrackingProcessed(LiveTrackingUpdate update)
    {
        _onLiveTrackingProcessed(update);
        if (update is not { StateChanged: true, Snapshot: not null })
        {
            return;
        }

        _presentation.Publish(update.Snapshot);
        _telemetry.TryQueue(update.Snapshot);
    }
}
