using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using IceCrow.Hearthstone.Logs;
using IceCrow.Overlay;

namespace IceCrow.App;

public partial class App : Application, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private LogConfigManager? _logConfigManager;
    private HearthstoneLogLocator? _logLocator;
    private OverlayHost? _overlayHost;
    private PowerLogTailer? _powerLogTailer;
    private Task? _logPipelineTask;
    private bool _disposed;
#if DEBUG
    private MainWindow? _developerWindow;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        _developerWindow = new MainWindow();
        _developerWindow.Closed += OnDeveloperWindowClosed;
        _developerWindow.Show();
#endif

        _overlayHost = new OverlayHost();
        _overlayHost.Start();

        _logLocator = new HearthstoneLogLocator();
        _logLocator.RecoverableError += ReportRecoverableLogError;
        _logConfigManager = new LogConfigManager();
        _powerLogTailer = new PowerLogTailer(_logLocator);
        _powerLogTailer.RecoverableError += ReportRecoverableLogError;
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
        _logPipelineTask = null;

        _overlayHost?.Dispose();
        _overlayHost = null;

#if DEBUG
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
        if (_logConfigManager is null || _logLocator is null || _powerLogTailer is null)
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
        var consumeTask = ConsumePowerLogAsync(_powerLogTailer, cancellationToken);
        await Task.WhenAll(tailTask, consumeTask).ConfigureAwait(false);
    }

    private async Task ConsumePowerLogAsync(
        PowerLogTailer tailer,
        CancellationToken cancellationToken)
    {
        await foreach (var line in tailer.Lines.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
#if DEBUG
            var developerWindow = _developerWindow;
            if (developerWindow is not null)
            {
                _ = developerWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () => developerWindow.AddPowerLogLine(line));
            }
#else
            _ = line;
#endif
        }
    }

    private void ReportRecoverableLogError(Exception exception)
    {
        Debug.WriteLine(exception);
        ReportLogStatus($"Power log waiting: {exception.Message}");
    }

    private void ReportLogStatus(string status)
    {
#if DEBUG
        var developerWindow = _developerWindow;
        if (developerWindow is not null)
        {
            _ = developerWindow.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => developerWindow.SetPowerLogStatus(status));
        }
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
