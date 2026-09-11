using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Decks;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Live;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;
#if DEBUG
using IceCrow.Overlay;
#endif

namespace IceCrow.App;

public partial class App : Application, IAsyncDisposable
{
    private IceCrowRuntime? _runtime;
    private Task? _stopTask;
    private HistoryWindow? _historyWindow;
    private CancellationTokenSource? _profileLinkCancellation;
#if DEBUG
    private MainWindow? _developerWindow;
    private DeveloperDiagnosticsPresenter? _developerDiagnosticsPresenter;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _historyWindow = new HistoryWindow();
        _historyWindow.Closed += OnHistoryWindowClosed;
        _historyWindow.LinkRequested += OnProfileLinkRequested;
        _historyWindow.UnlinkRequested += OnProfileUnlinkRequested;
        _historyWindow.CancelLinkRequested += OnProfileLinkCancelled;
        _historyWindow.VerificationPageRequested += OnVerificationPageRequested;
        _historyWindow.Show();

#if DEBUG
        _developerWindow = new MainWindow();
        _developerWindow.Closed += OnDeveloperWindowClosed;
        _developerWindow.Show();
#endif

        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IceCrow");
        var options = IceCrowRuntimeOptions.Load(localDataDirectory);
        _runtime = new IceCrowRuntime(
            localDataDirectory,
            Dispatcher,
            options,
            OnSessionProcessed,
            OnManacostDataStatusChanged,
            OnTelemetryStatusChanged,
            OnProfileSyncStatusChanged,
            OnHistoryChanged,
            OnCaptureStatusChanged,
            ReportRecoverableLogError,
            ReportLogStatus,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");

#if DEBUG
        var runtime = _runtime;
        _developerDiagnosticsPresenter = new DeveloperDiagnosticsPresenter(
            _developerWindow,
            _runtime.OverlayDiagnostics as OverlayRenderDiagnostics,
            () => runtime.TailerDiagnostics);
        _developerDiagnosticsPresenter.PublishDeckstringsStatus(ManacostDeckCodec.PackageVersion, "Ready");
        _developerDiagnosticsPresenter.PublishTelemetryStatus(false, 0, null);
        _developerWindow.CaptureToggleChanged += OnCaptureToggleChanged;
#endif

        _runtime.Start();
        _ = RunStartupCommandsAsync(e.Args);
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

    /// <summary>
    /// Device linking is an explicit user action started from the command
    /// line; the headless runtime keeps tracking while the user approves it.
    /// </summary>
    private async Task RunStartupCommandsAsync(string[] arguments)
    {
        if (_runtime?.ProfileSync is not { } profileSync)
        {
            return;
        }

        try
        {
            if (ProfileLinkCommand.IsUnlinkRequest(arguments))
            {
                await ProfileLinkCommand.UnlinkAsync(profileSync, CancellationToken.None);
            }
            else if (ProfileLinkCommand.IsLinkRequest(arguments))
            {
                _ = await ProfileLinkCommand.LinkAsync(profileSync, CancellationToken.None);
            }
            else if (CollectionImportCommand.IsRequest(arguments))
            {
                _ = await CollectionImportCommand.ExecuteAsync(profileSync, arguments, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            ReportLogStatus($"IceCrow command failed: {exception.GetType().Name}");
        }
    }

    private void OnSessionProcessed(GameSessionUpdate update)
    {
#if DEBUG
        if (update.Battlegrounds is { } battlegrounds)
        {
            _developerDiagnosticsPresenter?.Publish(battlegrounds);
        }
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

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The Debug build forwards status to the developer presenter instance.")]
    private void OnProfileSyncStatusChanged(ProfileSyncStatus status)
    {
        // Secret-free by construction; safe to trace.
        Debug.WriteLine($"Profile sync: {status.Phase}, pending={status.PendingEvents}, uploaded={status.UploadedEvents}");
        _ = Dispatcher.BeginInvoke(() => _historyWindow?.SetSyncStatus(status));
    }

    private void OnHistoryChanged(ProfileHistorySnapshot snapshot) =>
        _ = Dispatcher.BeginInvoke(() => _historyWindow?.ApplySnapshot(snapshot));

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
        _ = Dispatcher.BeginInvoke(() => _historyWindow?.SetRuntimeStatus(status));
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
        if (_historyWindow is not null)
        {
            _historyWindow.LinkRequested -= OnProfileLinkRequested;
            _historyWindow.UnlinkRequested -= OnProfileUnlinkRequested;
            _historyWindow.CancelLinkRequested -= OnProfileLinkCancelled;
            _historyWindow.VerificationPageRequested -= OnVerificationPageRequested;
            _historyWindow.Closed -= OnHistoryWindowClosed;
            _historyWindow = null;
        }
    }

#if DEBUG
    private void OnDeveloperWindowClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _developerDiagnosticsPresenter?.Dispose();
        _developerDiagnosticsPresenter = null;
        if (_developerWindow is not null)
        {
            _developerWindow.Closed -= OnDeveloperWindowClosed;
            _developerWindow = null;
        }
    }
#endif

    private async void OnHistoryWindowClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _profileLinkCancellation?.Cancel();
        await StopRuntimeAsync();
        Shutdown();
    }

    private async void OnProfileLinkRequested()
    {
        if (_runtime?.ProfileSync is not { } profileSync || _historyWindow is null)
        {
            _historyWindow?.SetLinkUpdate(new ProfileLinkUpdate(ProfileLinkStage.Unavailable));
            return;
        }

        _profileLinkCancellation?.Cancel();
        _profileLinkCancellation?.Dispose();
        _profileLinkCancellation = new CancellationTokenSource();
        var cancellation = _profileLinkCancellation;
        var workflow = new ProfileLinkWorkflow(new ProfileSyncLinkGateway(profileSync));
        try
        {
            await workflow.RunAsync(update =>
            {
                _ = Dispatcher.BeginInvoke(() => _historyWindow?.SetLinkUpdate(update));
                if (update.Stage == ProfileLinkStage.WaitingForApproval && update.VerificationUri is not null)
                {
                    ProfileLinkCommand.OpenVerificationPage(update.VerificationUri);
                }
            }, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _historyWindow?.SetLinkUpdate(new ProfileLinkUpdate(ProfileLinkStage.Cancelled));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Profile linking failed: {exception.GetType().Name}");
            _historyWindow?.SetLinkUpdate(new ProfileLinkUpdate(ProfileLinkStage.Unavailable));
        }
        finally
        {
            if (ReferenceEquals(_profileLinkCancellation, cancellation))
            {
                _profileLinkCancellation.Dispose();
                _profileLinkCancellation = null;
            }
        }
    }

    private async void OnProfileUnlinkRequested()
    {
        if (_runtime?.ProfileSync is not { } profileSync || _historyWindow is null)
        {
            return;
        }

        try
        {
            await profileSync.UnlinkAsync(CancellationToken.None);
            _historyWindow.SetLinkUpdate(new ProfileLinkUpdate(ProfileLinkStage.Cancelled));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Profile unlink failed: {exception.GetType().Name}");
            _historyWindow.SetLinkUpdate(new ProfileLinkUpdate(ProfileLinkStage.Unavailable));
        }
    }

    private void OnProfileLinkCancelled() => _profileLinkCancellation?.Cancel();

    private static void OnVerificationPageRequested(Uri uri) => ProfileLinkCommand.OpenVerificationPage(uri);
}
