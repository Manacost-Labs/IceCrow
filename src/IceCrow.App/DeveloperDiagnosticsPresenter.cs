using System.Windows.Threading;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;
using IceCrow.Infrastructure.ManacostApi;

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
    private ManacostDataStatus? _latestDataStatus;
    private (string PackageVersion, string Status)? _latestDeckstringsStatus;
    private (bool Consent, int Queued, DateTimeOffset? LastUpload)? _latestTelemetryStatus;
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

    public void PublishManacostDataStatus(ManacostDataStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            if (!_disposed)
            {
                _latestDataStatus = status;
            }
        }
    }

    public void PublishDeckstringsStatus(string packageVersion, string status)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _latestDeckstringsStatus = (packageVersion, status);
            }
        }
    }

    public void PublishTelemetryStatus(bool consent, int queued, DateTimeOffset? lastUpload)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _latestTelemetryStatus = (consent, queued, lastUpload);
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
            _latestDataStatus = null;
            _latestDeckstringsStatus = null;
            _latestTelemetryStatus = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        RawLogLine[] lines;
        LiveTrackingDiagnostics? diagnostics;
        string? status;
        ManacostDataStatus? dataStatus;
        (string PackageVersion, string Status)? deckstringsStatus;
        (bool Consent, int Queued, DateTimeOffset? LastUpload)? telemetryStatus;
        lock (_gate)
        {
            lines = _pendingLines.ToArray();
            _pendingLines.Clear();
            diagnostics = _latestDiagnostics;
            _latestDiagnostics = null;
            status = _latestStatus;
            _latestStatus = null;
            dataStatus = _latestDataStatus;
            _latestDataStatus = null;
            deckstringsStatus = _latestDeckstringsStatus;
            _latestDeckstringsStatus = null;
            telemetryStatus = _latestTelemetryStatus;
            _latestTelemetryStatus = null;
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

        if (dataStatus is not null)
        {
            _window.SetManacostDataStatus(dataStatus);
        }

        if (deckstringsStatus is { } deckstrings)
        {
            _window.SetDeckstringsStatus(deckstrings.PackageVersion, deckstrings.Status);
        }

        if (telemetryStatus is { } telemetry)
        {
            _window.SetTelemetryStatus(telemetry.Consent, telemetry.Queued, telemetry.LastUpload);
        }
    }
}
#endif
