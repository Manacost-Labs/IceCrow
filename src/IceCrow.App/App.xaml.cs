using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using IceCrow.Hearthstone.Data;
using IceCrow.Hearthstone.Decks;
using IceCrow.Hearthstone.Logs;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Live;
using IceCrow.Overlay;
using IceCrow.Telemetry;

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
    private HttpClient? _manacostHttpClient;
    private InMemoryCardDatabase? _cardDatabase;
    private ManacostDataSynchronizer? _dataSynchronizer;
    private Task? _dataPipelineTask;
    private TelemetryConsent? _telemetryConsent;
    private TelemetryOutbox? _telemetryOutbox;
    private TelemetryPreferencesStore? _telemetryPreferencesStore;
    private Task? _telemetryPreferencesTask;
    private Task? _telemetryQueueTask;
    private long _lastTelemetryRevision = -1;
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

        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IceCrow");
        _cardDatabase = new InMemoryCardDatabase();
        _manacostHttpClient = new HttpClient();
        var dataStore = new JsonHearthstoneDataStore(Path.Combine(localDataDirectory, "data", "hearthstone-data.json"));
        var datasetClient = new ManacostDatasetClient(_manacostHttpClient);
        _dataSynchronizer = new ManacostDataSynchronizer(_cardDatabase, dataStore, datasetClient);
        _dataSynchronizer.StatusChanged += OnManacostDataStatusChanged;
        _dataPipelineTask = RunDataPipelineAsync(_shutdown.Token);

        _telemetryConsent = new TelemetryConsent();
        _telemetryOutbox = new TelemetryOutbox(Path.Combine(localDataDirectory, "telemetry", "outbox.json"));
        _telemetryPreferencesStore = new TelemetryPreferencesStore(
            Path.Combine(localDataDirectory, "telemetry", "preferences.json"));
        _telemetryPreferencesTask = LoadTelemetryPreferencesAsync(_shutdown.Token);
#if DEBUG
        _developerDiagnosticsPresenter?.PublishDeckstringsStatus(ManacostDeckCodec.PackageVersion, "Ready");
        _developerDiagnosticsPresenter?.PublishTelemetryStatus(false, 0, null);
#endif

        _overlayHost = new OverlayHost();
        _overlayHost.Start();
        _liveOverlayPresenter = new LiveOverlayPresenter(_overlayHost, Dispatcher, _cardDatabase);

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

        if (_dataPipelineTask is not null)
        {
            try
            {
                _dataPipelineTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_telemetryPreferencesTask is not null)
        {
            try
            {
                _telemetryPreferencesTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_telemetryQueueTask is not null)
        {
            try
            {
                _telemetryQueueTask.GetAwaiter().GetResult();
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

        if (_dataSynchronizer is not null)
        {
            _dataSynchronizer.StatusChanged -= OnManacostDataStatusChanged;
        }

        _dataSynchronizer = null;
        _cardDatabase = null;
        _dataPipelineTask = null;
        _telemetryOutbox?.Dispose();
        _telemetryOutbox = null;
        _telemetryConsent = null;
        _telemetryPreferencesStore = null;
        _telemetryPreferencesTask = null;
        _telemetryQueueTask = null;
        _manacostHttpClient?.Dispose();
        _manacostHttpClient = null;

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
            QueueTelemetryIfEligible(update.Snapshot);
        }
    }

    private async Task RunDataPipelineAsync(CancellationToken cancellationToken)
    {
        if (_dataSynchronizer is null)
        {
            return;
        }

        await _dataSynchronizer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _dataSynchronizer.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnManacostDataStatusChanged(ManacostDataStatus status)
    {
#if DEBUG
        _developerDiagnosticsPresenter?.PublishManacostDataStatus(status);
#else
        Debug.WriteLine($"Manacost data: {status.DataVersion ?? "no cache"}; offline={status.OfflineMode}");
#endif
    }

    private void QueueTelemetryIfEligible(IceCrow.Tracking.TrackingSnapshot snapshot)
    {
        if (_telemetryConsent?.IsEnabled != true ||
            _telemetryOutbox is null ||
            snapshot.Revision == Interlocked.Read(ref _lastTelemetryRevision))
        {
            return;
        }

        var summary = MatchSummaryFactory.Create(
            snapshot,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        if (summary is null)
        {
            return;
        }

        Interlocked.Exchange(ref _lastTelemetryRevision, snapshot.Revision);
        _telemetryQueueTask = QueueTelemetryCoreAsync(summary, _shutdown.Token);
    }

    private async Task QueueTelemetryCoreAsync(MatchSummary summary, CancellationToken cancellationToken)
    {
        if (_telemetryOutbox is null || _telemetryConsent is null)
        {
            return;
        }

        try
        {
            await _telemetryOutbox.EnqueueAsync(summary, _telemetryConsent, cancellationToken).ConfigureAwait(false);
#if DEBUG
            var count = await _telemetryOutbox.CountAsync(cancellationToken).ConfigureAwait(false);
            _developerDiagnosticsPresenter?.PublishTelemetryStatus(
                _telemetryConsent.IsEnabled,
                count,
                null);
#endif
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Telemetry outbox unavailable: {exception.Message}");
        }
    }

    private async Task LoadTelemetryPreferencesAsync(CancellationToken cancellationToken)
    {
        if (_telemetryPreferencesStore is null || _telemetryConsent is null)
        {
            return;
        }

        try
        {
            var preferences = await _telemetryPreferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            _telemetryConsent.SetEnabled(preferences.ShareAnonymousGameplayStatistics);
#if DEBUG
            var count = _telemetryOutbox is null
                ? 0
                : await _telemetryOutbox.CountAsync(cancellationToken).ConfigureAwait(false);
            _developerDiagnosticsPresenter?.PublishTelemetryStatus(_telemetryConsent.IsEnabled, count, null);
#endif
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Telemetry preferences unavailable; consent remains off: {exception.Message}");
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
