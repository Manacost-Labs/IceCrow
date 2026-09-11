using System.Threading.Channels;

namespace IceCrow.ProfileSync;

/// <summary>
/// Single persistence worker between the live producers and the durable
/// outbox. A producer hands an event over without blocking; the worker owns
/// it until <see cref="ProfileOutbox.EnqueueAsync"/> commits it, retrying
/// transient IO failures with backoff instead of dropping. When the handoff
/// is full the producer gets an explicit <see cref="ProfileHandoffResult.Full"/>
/// and the oldest accepted events stay queued. Shutdown completes the
/// producer side and drains everything accepted within a bounded grace period.
/// </summary>
public sealed class ProfilePersistenceWorker : IDisposable
{
    public const int DefaultCapacity = 64;
    public const int MaximumCapacity = 512;

    private readonly ProfileOutbox _outbox;
    private readonly Channel<ProfileEvent> _queue;
    private readonly TimeSpan _minimumRetry;
    private readonly TimeSpan _maximumRetry;
    private readonly TimeSpan _drainGrace;
    private readonly CancellationTokenSource _drainDeadline = new();
    private ProfileHandoffStatus _status = ProfileHandoffStatus.Initial;
    private bool _closed;
    private long _accepted;
    private long _persisted;
    private long _duplicates;
    private long _refusedFull;
    private long _retries;
    private int _pending;

    public ProfilePersistenceWorker(
        ProfileOutbox outbox,
        int capacity = DefaultCapacity,
        TimeSpan? minimumRetry = null,
        TimeSpan? maximumRetry = null,
        TimeSpan? drainGrace = null)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _outbox = outbox;
        _minimumRetry = minimumRetry ?? TimeSpan.FromMilliseconds(250);
        _maximumRetry = maximumRetry ?? TimeSpan.FromSeconds(30);
        _drainGrace = drainGrace ?? TimeSpan.FromSeconds(10);
        if (_minimumRetry <= TimeSpan.Zero || _maximumRetry < _minimumRetry || _drainGrace <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRetry), "Retry and drain windows must be positive and ordered.");
        }

        _queue = Channel.CreateBounded<ProfileEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public event Action<ProfileHandoffStatus>? StatusChanged;

    /// <summary>Raised on the worker after each durable commit, so the uploader can wake.</summary>
    public event Action<ProfileEvent>? Persisted;

    public ProfileHandoffStatus Status => Volatile.Read(ref _status);

    /// <summary>Non-blocking handoff from the gameplay thread.</summary>
    public ProfileHandoffResult Accept(ProfileEvent profileEvent)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        if (Volatile.Read(ref _closed))
        {
            return ProfileHandoffResult.Closed;
        }

        if (_queue.Writer.TryWrite(profileEvent))
        {
            Interlocked.Increment(ref _accepted);
            Interlocked.Increment(ref _pending);
            Publish(Status.Phase == ProfileHandoffPhase.Full ? ProfileHandoffPhase.Idle : Status.Phase);
            return ProfileHandoffResult.Accepted;
        }

        Interlocked.Increment(ref _refusedFull);
        Publish(ProfileHandoffPhase.Full, "handoff full");
        return ProfileHandoffResult.Full;
    }

    /// <summary>
    /// Stops accepting new events. Everything already accepted is still
    /// drained by <see cref="RunAsync"/> within the grace period.
    /// </summary>
    public void Complete()
    {
        Volatile.Write(ref _closed, true);
        if (_queue.Writer.TryComplete())
        {
            Publish(ProfileHandoffPhase.Draining);
            _drainDeadline.CancelAfter(_drainGrace);
        }
    }

    /// <summary>
    /// Runs until the producer side is completed and every accepted event is
    /// committed, or until the drain grace expires. <paramref name="cancellationToken"/>
    /// only completes the producer side; it never abandons an accepted event
    /// before the grace period.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Complete);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_drainDeadline.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryPeek(out var profileEvent))
                {
                    await PersistWithRetryAsync(profileEvent, _drainDeadline.Token).ConfigureAwait(false);
                    _ = _queue.Reader.TryRead(out _);
                    Interlocked.Decrement(ref _pending);
                    Publish(Status.Phase);
                }
            }

            Publish(ProfileHandoffPhase.Stopped);
        }
        catch (OperationCanceledException) when (_drainDeadline.IsCancellationRequested)
        {
            Publish(ProfileHandoffPhase.Stopped, "drain grace expired with events pending");
        }
    }

    public void Dispose()
    {
        _drainDeadline.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PersistWithRetryAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            Publish(attempt == 0 ? ProfileHandoffPhase.Persisting : ProfileHandoffPhase.Retrying);
            try
            {
                var result = await _outbox.EnqueueAsync(profileEvent, cancellationToken).ConfigureAwait(false);
                switch (result)
                {
                    case ProfileOutboxResult.Enqueued:
                    case ProfileOutboxResult.Replaced:
                        Interlocked.Increment(ref _persisted);
                        Persisted?.Invoke(profileEvent);
                        Publish(ProfileHandoffPhase.Idle);
                        return;
                    case ProfileOutboxResult.Duplicate:
                        // A retry after a commit that threw late; the event is already durable.
                        Interlocked.Increment(ref _duplicates);
                        Publish(ProfileHandoffPhase.Idle);
                        return;
                    case ProfileOutboxResult.Full:
                        // Capacity, not corruption: hold the event and try again
                        // once acknowledgements free space.
                        Publish(ProfileHandoffPhase.Full, "outbox full");
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported outbox result '{result}'.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Publish(ProfileHandoffPhase.Retrying, exception.GetType().Name);
            }

            attempt++;
            Interlocked.Increment(ref _retries);
            await Task.Delay(ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        var scaled = _minimumRetry.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 12));
        var capped = Math.Min(scaled, _maximumRetry.TotalMilliseconds);
        var jitter = 1 + ((Random.Shared.NextDouble() * 0.4) - 0.2);
        return TimeSpan.FromMilliseconds(Math.Clamp(capped * jitter, _minimumRetry.TotalMilliseconds, _maximumRetry.TotalMilliseconds));
    }

    private void Publish(ProfileHandoffPhase phase, string? failure = null)
    {
        var effectiveFailure = failure ?? (phase is ProfileHandoffPhase.Retrying or ProfileHandoffPhase.Full
            ? Status.LastFailure
            : null);
        var status = new ProfileHandoffStatus(
            phase,
            Interlocked.Read(ref _accepted),
            Interlocked.Read(ref _persisted),
            Interlocked.Read(ref _duplicates),
            Interlocked.Read(ref _refusedFull),
            Interlocked.Read(ref _retries),
            Volatile.Read(ref _pending),
            effectiveFailure);
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }
}
