using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using IceCrow.Telemetry;
using IceCrow.Tracking;

namespace IceCrow.App.Runtime;

internal sealed class TelemetryRuntime : IAsyncDisposable
{
    private const int QueueCapacity = 16;
    private readonly TelemetryConsent _consent = new();
    private readonly TelemetryOutbox _outbox;
    private readonly TelemetryPreferencesStore _preferencesStore;
    private readonly Channel<MatchSummary> _queue;
    private readonly Action<bool, int, DateTimeOffset?> _onStatusChanged;
    private readonly string _clientVersion;
    private long _lastRevision = -1;

    public TelemetryRuntime(
        string localDataDirectory,
        string clientVersion,
        Action<bool, int, DateTimeOffset?> onStatusChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);
        ArgumentNullException.ThrowIfNull(onStatusChanged);
        _clientVersion = clientVersion;
        _onStatusChanged = onStatusChanged;
        _outbox = new TelemetryOutbox(Path.Combine(localDataDirectory, "telemetry", "outbox.json"));
        _preferencesStore = new TelemetryPreferencesStore(
            Path.Combine(localDataDirectory, "telemetry", "preferences.json"));
        _queue = Channel.CreateBounded<MatchSummary>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await LoadPreferencesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var summary in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await EnqueueAsync(summary, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void TryQueue(TrackingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_consent.IsEnabled || snapshot.Revision == Interlocked.Read(ref _lastRevision))
        {
            return;
        }

        var summary = MatchSummaryFactory.Create(snapshot, _clientVersion);
        if (summary is null || !_queue.Writer.TryWrite(summary))
        {
            return;
        }

        Interlocked.Exchange(ref _lastRevision, snapshot.Revision);
    }

    public void Complete() => _queue.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        _outbox.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task LoadPreferencesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            _consent.SetEnabled(preferences.ShareAnonymousGameplayStatistics);
            var count = await _outbox.CountAsync(cancellationToken).ConfigureAwait(false);
            _onStatusChanged(_consent.IsEnabled, count, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Telemetry preferences unavailable; consent remains off: {exception.Message}");
        }
    }

    private async Task EnqueueAsync(MatchSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            await _outbox.EnqueueAsync(summary, _consent, cancellationToken).ConfigureAwait(false);
            var count = await _outbox.CountAsync(cancellationToken).ConfigureAwait(false);
            _onStatusChanged(_consent.IsEnabled, count, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Telemetry outbox unavailable: {exception.Message}");
        }
    }
}
