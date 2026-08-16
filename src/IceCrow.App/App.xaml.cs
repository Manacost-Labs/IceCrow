using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Decks;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Live;

namespace IceCrow.App;

public partial class App : Application, IAsyncDisposable
{
    private IceCrowRuntime? _runtime;
    private Task? _stopTask;
#if DEBUG
    private MainWindow? _developerWindow;
    private DeveloperDiagnosticsPresenter? _developerDiagnosticsPresenter;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        _developerWindow = new MainWindow();
        _developerWindow.Closed += OnDeveloperWindowClosed;
        _developerWindow.Show();
#endif

        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IceCrow");
        _runtime = new IceCrowRuntime(
            localDataDirectory,
            Dispatcher,
            OnLiveTrackingProcessed,
            OnManacostDataStatusChanged,
            OnTelemetryStatusChanged,
            OnCaptureStatusChanged,
            ReportRecoverableLogError,
            ReportLogStatus,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");

#if DEBUG
        var runtime = _runtime;
        _developerDiagnosticsPresenter = new DeveloperDiagnosticsPresenter(
            _developerWindow,
            _runtime.OverlayDiagnostics,
            () => runtime.TailerDiagnostics);
        _developerDiagnosticsPresenter.PublishDeckstringsStatus(ManacostDeckCodec.PackageVersion, "Ready");
        _developerDiagnosticsPresenter.PublishTelemetryStatus(false, 0, null);
        _developerWindow.CaptureToggleChanged += OnCaptureToggleChanged;
#endif

        _runtime.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // WPF exposes a synchronous final callback. Normal Debug-window shutdown
        // awaits StopRuntimeAsync; this is the idempotent fallback for OS/app exits.
        var stopTask = _stopTask;
        if (_runtime is not null)
        {
            _runtime.PrepareForSynchronousExit();
            if (stopTask is null)
            {
                stopTask = _runtime.DisposeAsync().AsTask();
                _runtime = null;
            }
        }

        stopTask?.GetAwaiter().GetResult();
        DisposeDiagnostics();
        base.OnExit(e);
    }

    public async ValueTask DisposeAsync()
    {
        await StopRuntimeAsync();
        GC.SuppressFinalize(this);
    }

    private Task StopRuntimeAsync() => _stopTask ??= StopRuntimeCoreAsync();

    private async Task StopRuntimeCoreAsync()
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
            _runtime = null;
        }

        DisposeDiagnostics();
    }

    private void OnLiveTrackingProcessed(LiveTrackingUpdate update)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.Publish(update);
#else
        _ = update;
#endif
    }

    private void OnManacostDataStatusChanged(ManacostDataStatus status)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.PublishManacostDataStatus(status);
#else
        Debug.WriteLine($"Manacost data: {status.DataVersion ?? "no cache"}; offline={status.OfflineMode}");
#endif
    }

    private void OnCaptureStatusChanged(RecordingCaptureStatus status)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.PublishCaptureStatus(status);
#else
        _ = status;
#endif
    }

#if DEBUG
    private void OnCaptureToggleChanged(bool enabled) => _runtime?.SetCaptureEnabled(enabled);
#endif

    private void OnTelemetryStatusChanged(bool consent, int queued, DateTimeOffset? lastUpload)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.PublishTelemetryStatus(consent, queued, lastUpload);
#else
        _ = consent;
        _ = queued;
        _ = lastUpload;
#endif
    }

    private void ReportRecoverableLogError(Exception exception)
    {
        Debug.WriteLine(exception);
        ReportLogStatus($"Power log waiting: {exception.Message}");
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The Debug build forwards status to the developer presenter instance.")]
    private void ReportLogStatus(string status)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.PublishStatus(status);
#else
        Debug.WriteLine(status);
#endif
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The Debug build disposes instance-owned developer diagnostics.")]
    private void DisposeDiagnostics()
    {
#if DEBUG
        _developerDiagnosticsPresenter?.Dispose();
        _developerDiagnosticsPresenter = null;
        if (_developerWindow is not null)
        {
            _developerWindow.CaptureToggleChanged -= OnCaptureToggleChanged;
            _developerWindow.Closed -= OnDeveloperWindowClosed;
            _developerWindow = null;
        }
#endif
    }

#if DEBUG
    private async void OnDeveloperWindowClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        await StopRuntimeAsync();
        Shutdown();
    }
#endif
}
