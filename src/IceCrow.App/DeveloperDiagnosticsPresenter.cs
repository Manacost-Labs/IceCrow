using System.Windows.Threading;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;

namespace IceCrow.App;

#if DEBUG
internal sealed class DeveloperDiagnosticsPresenter : IDisposable
{
    private const int MaximumPendingLines = 20;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly MainWindow _window;
    private readonly DispatcherTimer _timer;
    private readonly Queue<RawLogLine> _pendingLines = [];
    private LiveTrackingDiagnostics? _latestDiagnostics;
    private string? _latestStatus;
    private bool _disposed;

    public DeveloperDiagnosticsPresenter(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _timer = new DispatcherTimer(
            RefreshInterval,
            DispatcherPriority.Background,
            OnTimerTick,
            window.Dispatcher);
        _timer.Start();
    }

    public void Publish(LiveTrackingUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            while (_pendingLines.Count >= MaximumPendingLines)
            {
                _pendingLines.Dequeue();
            }

            _pendingLines.Enqueue(update.RawLine);
            _latestDiagnostics = update.Diagnostics;
        }
    }

    public void PublishStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        lock (_gate)
        {
            if (!_disposed)
            {
                _latestStatus = status;
            }
        }
    }

    public void Dispose()
    {
        _window.Dispatcher.VerifyAccess();
        _timer.Stop();
        lock (_gate)
        {
            _disposed = true;
            _pendingLines.Clear();
            _latestDiagnostics = null;
            _latestStatus = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        RawLogLine[] lines;
        LiveTrackingDiagnostics? diagnostics;
        string? status;
        lock (_gate)
        {
            lines = _pendingLines.ToArray();
            _pendingLines.Clear();
            diagnostics = _latestDiagnostics;
            _latestDiagnostics = null;
            status = _latestStatus;
            _latestStatus = null;
        }

        foreach (var line in lines)
        {
            _window.AddPowerLogLine(line);
        }

        if (diagnostics is not null)
        {
            _window.SetLiveTrackingDiagnostics(diagnostics);
        }

        if (status is not null)
        {
            _window.SetPowerLogStatus(status);
        }
    }
}
#endif
