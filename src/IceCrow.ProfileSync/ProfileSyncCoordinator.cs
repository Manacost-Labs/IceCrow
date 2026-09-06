namespace IceCrow.ProfileSync;

/// <summary>
/// Single background uploader for the profile outbox. It never runs per
/// gameplay event: it wakes on <see cref="Notify"/> or on the idle interval,
/// holds while a match is active, uploads bounded batches, and backs off
/// exponentially with jitter on failure. Match history is only ever removed
/// after the server acknowledged it or permanently rejected it.
/// </summary>
public sealed class ProfileSyncCoordinator : IDisposable
{
    private readonly ProfileOutbox _outbox;
    private readonly IProfileSyncTransport _transport;
    private readonly Func<CancellationToken, ValueTask<bool>> _isLinkedAsync;
    private readonly ProfileSyncOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private ProfileSyncStatus _status = ProfileSyncStatus.Initial;
    private bool _gameplayActive;
    private long _overflowRejections;

    public ProfileSyncCoordinator(
        ProfileOutbox outbox,
        IProfileSyncTransport transport,
        Func<CancellationToken, ValueTask<bool>> isLinkedAsync,
        ProfileSyncOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(isLinkedAsync);
        _options = options ?? ProfileSyncOptions.Default;
        _options.Validate();
        _outbox = outbox;
        _transport = transport;
        _isLinkedAsync = isLinkedAsync;
        _time = timeProvider ?? TimeProvider.System;
    }

    public event Action<ProfileSyncStatus>? StatusChanged;

    public ProfileSyncStatus Status => Volatile.Read(ref _status);

    /// <summary>Wakes the uploader; safe to call from any thread, coalesces repeated calls.</summary>
    public void Notify()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; one drain observes every queued event.
        }
    }

    public void SetGameplayActive(bool active)
    {
        Volatile.Write(ref _gameplayActive, active);
        if (!active)
        {
            Notify();
        }
    }

    /// <summary>The outbox refused a history event because it is full; count it visibly.</summary>
    public void ReportOutboxOverflow()
    {
        Interlocked.Increment(ref _overflowRejections);
        Publish(Status with { OutboxOverflowRejections = Interlocked.Read(ref _overflowRejections) });
    }

    /// <summary>Called after the device was linked again so uploads resume.</summary>
    public void ResetAuthorization()
    {
        if (Status.Phase == ProfileSyncPhase.AuthorizationRequired)
        {
            Publish(Status with { Phase = ProfileSyncPhase.Idle, ConsecutiveFailures = 0, RetryAt = null });
        }

        Notify();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _wake.WaitAsync(ComputeWait(), cancellationToken).ConfigureAwait(false);
                await UploadPendingSafelyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// A corrupt, oversized, or locked outbox file must not kill the only
    /// uploader for the rest of the process; it becomes a counted backoff
    /// so the next tick retries after the file is repaired or replaced.
    /// </summary>
    public async Task UploadPendingSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UploadPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            var failures = Status.ConsecutiveFailures + 1;
            Publish(Status with
            {
                Phase = ProfileSyncPhase.BackingOff,
                ConsecutiveFailures = failures,
                RetryAt = _time.GetUtcNow() + ComputeBackoff(failures),
            });
        }
    }

    /// <summary>Uploads batches until the outbox is drained or a batch is not accepted.</summary>
    public async Task UploadPendingAsync(CancellationToken cancellationToken)
    {
        if (!await _isLinkedAsync(cancellationToken).ConfigureAwait(false))
        {
            Publish(Status with { Phase = ProfileSyncPhase.NotLinked, PendingEvents = await CountAsync(cancellationToken).ConfigureAwait(false) });
            return;
        }

        if (Status.Phase == ProfileSyncPhase.AuthorizationRequired ||
            (_options.HoldDuringGameplay && Volatile.Read(ref _gameplayActive)) ||
            Status.RetryAt is { } retryAt && retryAt > _time.GetUtcNow())
        {
            return;
        }

        while (await UploadOnceAsync(cancellationToken).ConfigureAwait(false) == ProfileUploadStatus.Accepted)
        {
        }
    }

    /// <summary>Uploads one batch; returns null when nothing was pending.</summary>
    public async Task<ProfileUploadStatus?> UploadOnceAsync(CancellationToken cancellationToken)
    {
        var batch = await _outbox.PeekBatchAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            Publish(Status with { Phase = ProfileSyncPhase.Idle, PendingEvents = 0, ConsecutiveFailures = 0, RetryAt = null });
            return null;
        }

        Publish(Status with { Phase = ProfileSyncPhase.Uploading, PendingEvents = await CountAsync(cancellationToken).ConfigureAwait(false) });
        var result = await _transport.UploadAsync(batch, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        switch (result.Status)
        {
            case ProfileUploadStatus.Accepted:
                await ApplyAcceptedAsync(batch, result, now, cancellationToken).ConfigureAwait(false);
                break;
            case ProfileUploadStatus.Unauthorized:
                Publish(Status with { Phase = ProfileSyncPhase.AuthorizationRequired, RetryAt = null });
                break;
            case ProfileUploadStatus.RateLimited:
            case ProfileUploadStatus.Unavailable:
                var failures = Status.ConsecutiveFailures + 1;
                var delay = result.RetryAfter ?? ComputeBackoff(failures);
                Publish(Status with
                {
                    Phase = ProfileSyncPhase.BackingOff,
                    ConsecutiveFailures = failures,
                    RetryAt = now + delay,
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported upload status '{result.Status}'.");
        }

        return result.Status;
    }

    /// <summary>Exponential backoff capped by the options, with ±20% jitter.</summary>
    public TimeSpan ComputeBackoff(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);
        var exponent = Math.Min(consecutiveFailures - 1, 16);
        var scaled = _options.EffectiveMinimumBackoff.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(scaled, _options.EffectiveMaximumBackoff.TotalMilliseconds);
        var jitter = 1 + ((Random.Shared.NextDouble() * 0.4) - 0.2);
        var withJitter = Math.Clamp(
            capped * jitter,
            _options.EffectiveMinimumBackoff.TotalMilliseconds,
            _options.EffectiveMaximumBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(withJitter);
    }

    public void Dispose()
    {
        _wake.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ApplyAcceptedAsync(
        IReadOnlyList<ProfileEvent> batch,
        ProfileUploadResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var submitted = batch.Select(static item => item.EventId).ToHashSet();
        var acknowledged = result.AcknowledgedEventIds.Where(submitted.Contains).Distinct().ToArray();
        var rejected = result.PermanentlyRejectedEventIds
            .Where(id => submitted.Contains(id) && !acknowledged.Contains(id))
            .Distinct()
            .ToArray();
        await _outbox.RemoveAsync(acknowledged.Concat(rejected), cancellationToken).ConfigureAwait(false);
        var pending = await CountAsync(cancellationToken).ConfigureAwait(false);
        var current = Status;
        Publish(current with
        {
            Phase = ProfileSyncPhase.Idle,
            PendingEvents = pending,
            UploadedEvents = current.UploadedEvents + acknowledged.Length,
            PermanentlyRejectedEvents = current.PermanentlyRejectedEvents + rejected.Length,
            ConsecutiveFailures = acknowledged.Length + rejected.Length == batch.Count ? 0 : current.ConsecutiveFailures + 1,
            LastUploadAt = now,
            RetryAt = null,
        });
    }

    private TimeSpan ComputeWait()
    {
        var status = Status;
        if (status.RetryAt is { } retryAt)
        {
            var remaining = retryAt - _time.GetUtcNow();
            if (remaining > TimeSpan.Zero && remaining < _options.EffectiveIdleInterval)
            {
                return remaining;
            }
        }

        return _options.EffectiveIdleInterval;
    }

    private Task<int> CountAsync(CancellationToken cancellationToken) => _outbox.CountAsync(cancellationToken);

    private void Publish(ProfileSyncStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }
}
