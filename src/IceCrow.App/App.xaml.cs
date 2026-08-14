using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Windows;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;
using IceCrow.Overlay;

namespace IceCrow.App;

public partial class App : Application, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private LogConfigManager? _logConfigManager;
    private HearthstoneLogLocator? _logLocator;
    private OverlayHost? _overlayHost;
    private PowerLogTailer? _powerLogTailer;
    private LiveTrackingCoordinator? _liveTrackingCoordinator;
    private LiveOverlayPresenter? _liveOverlayPresenter;
    private Task? _logPipelineTask;
    private bool _disposed;
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
        _developerDiagnosticsPresenter = new DeveloperDiagnosticsPresenter(_developerWindow);
#endif

        _overlayHost = new OverlayHost();
        _overlayHost.Start();
        _liveOverlayPresenter = new LiveOverlayPresenter(_overlayHost, Dispatcher);

        _logLocator = new HearthstoneLogLocator();
        _logLocator.RecoverableError += ReportRecoverableLogError;
        _logConfigManager = new LogConfigManager();
        _powerLogTailer = new PowerLogTailer(_logLocator);
        _powerLogTailer.RecoverableError += ReportRecoverableLogError;
        _liveTrackingCoordinator = new LiveTrackingCoordinator();
        _logPipelineTask = RunLogPipelineAsync(_shutdown.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        if (_logPipelineTask is not null)
        {
            try
            {
                _logPipelineTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_logLocator is not null)
        {
            _logLocator.RecoverableError -= ReportRecoverableLogError;
        }

        if (_powerLogTailer is not null)
        {
            _powerLogTailer.RecoverableError -= ReportRecoverableLogError;
        }

        _logConfigManager?.Dispose();
        _logConfigManager = null;
        _logLocator = null;
        _powerLogTailer = null;
        _liveTrackingCoordinator = null;
        _logPipelineTask = null;

        _liveOverlayPresenter?.Dispose();
        _liveOverlayPresenter = null;
        _overlayHost?.Dispose();
        _overlayHost = null;

#if DEBUG
        _developerDiagnosticsPresenter?.Dispose();
        _developerDiagnosticsPresenter = null;
        if (_developerWindow is not null)
        {
            _developerWindow.Closed -= OnDeveloperWindowClosed;
            _developerWindow = null;
        }
#endif

        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunLogPipelineAsync(CancellationToken cancellationToken)
    {
        if (_logConfigManager is null ||
            _logLocator is null ||
            _powerLogTailer is null ||
            _liveTrackingCoordinator is null)
        {
            return;
        }

        try
        {
            var changed = await _logConfigManager
                .EnsurePowerLoggingAsync(_logLocator, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            ReportLogStatus(changed
                ? "Power logging configured. Hearthstone may need a restart before new settings take effect."
                : "Power logging configuration is ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            ReportRecoverableLogError(exception);
        }

        var tailTask = _powerLogTailer.RunAsync(cancellationToken);
        var consumeTask = _liveTrackingCoordinator.RunAsync(
            _powerLogTailer.Lines,
            OnLiveTrackingProcessed,
            cancellationToken);
        await Task.WhenAll(tailTask, consumeTask).ConfigureAwait(false);
    }

    private void OnLiveTrackingProcessed(LiveTrackingUpdate update)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.Publish(update);
#endif
        if (update is { StateChanged: true, Snapshot: not null })
        {
            _liveOverlayPresenter?.Publish(update.Snapshot);
        }
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

#if DEBUG
    private void OnDeveloperWindowClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        Shutdown();
    }
#endif
}
