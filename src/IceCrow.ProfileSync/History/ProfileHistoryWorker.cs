using System.Threading.Channels;

namespace IceCrow.ProfileSync.History;

/// <summary>
/// Single bounded persistence owner between live match completion and the
/// local history file. Accepted items are retried and drained on shutdown.
/// </summary>
public sealed class ProfileHistoryWorker : IDisposable
{
    public const int DefaultCapacity = 64;
    private readonly ProfileHistoryStore _store;
    private readonly Channel<ProfileEvent> _queue;
    private readonly CancellationTokenSource _drainDeadline = new();
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _drainGrace;
    private bool _closed;

    public ProfileHistoryWorker(
        ProfileHistoryStore store,
        int capacity = DefaultCapacity,
        TimeSpan? retryDelay = null,
        TimeSpan? drainGrace = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (capacity is < 1 or > ProfilePersistenceWorker.MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _store = store;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
        _drainGrace = drainGrace ?? TimeSpan.FromSeconds(10);
        if (_retryDelay <= TimeSpan.Zero || _drainGrace <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _queue = Channel.CreateBounded<ProfileEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public event Action<ProfileHistorySnapshot>? SnapshotChanged;

    /// <summary>Secret-free recoverable persistence failure for diagnostics.</summary>
    public event Action<string>? PersistenceUnavailable;

    public ProfileHandoffResult Accept(ProfileEvent profileEvent)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        if (Volatile.Read(ref _closed))
        {
            return ProfileHandoffResult.Closed;
        }

        return _queue.Writer.TryWrite(profileEvent)
            ? ProfileHandoffResult.Accepted
            : ProfileHandoffResult.Full;
    }

    public void Complete()
    {
        Volatile.Write(ref _closed, true);
        if (_queue.Writer.TryComplete())
        {
            _drainDeadline.CancelAfter(_drainGrace);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Complete);
        await PublishSnapshotWithRetryAsync(_drainDeadline.Token).ConfigureAwait(false);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_drainDeadline.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryPeek(out var profileEvent))
                {
                    await PersistWithRetryAsync(profileEvent, _drainDeadline.Token).ConfigureAwait(false);
                    _ = _queue.Reader.TryRead(out _);
                }
            }
        }
        catch (OperationCanceledException) when (_drainDeadline.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _drainDeadline.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PersistWithRetryAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var result = await _store.AppendAsync(profileEvent, cancellationToken).ConfigureAwait(false);
                if (result == ProfileHistoryAppendResult.Full)
                {
                    throw new IOException("The local match history is full.");
                }

                if (result == ProfileHistoryAppendResult.Added)
                {
                    SnapshotChanged?.Invoke(await _store.ReadAsync(cancellationToken).ConfigureAwait(false));
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                PersistenceUnavailable?.Invoke(exception.GetType().Name);
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PublishSnapshotWithRetryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                SnapshotChanged?.Invoke(await _store.ReadAsync(cancellationToken).ConfigureAwait(false));
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                PersistenceUnavailable?.Invoke(exception.GetType().Name);
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
