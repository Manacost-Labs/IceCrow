namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// Failure isolation for an optional client-state source. A provider exception
/// becomes a null snapshot plus a bounded failure counter so that a broken or
/// unsupported adapter can never stop tracking. Cancellation requested by the
/// caller is the only exception that escapes.
/// </summary>
public sealed class ClientStateSourceGuard<TSnapshot> : IClientStateSource<TSnapshot>
    where TSnapshot : class
{
    public const long MaximumCountedFailures = 1_000_000;

    private readonly IClientStateSource<TSnapshot> _source;
    private long _failureCount;
    private Exception? _lastError;

    public ClientStateSourceGuard(IClientStateSource<TSnapshot> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Total isolated provider failures, saturating at <see cref="MaximumCountedFailures"/>.</summary>
    public long FailureCount => Interlocked.Read(ref _failureCount);

    /// <summary>The most recent isolated failure; null once a read succeeds again.</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    public ClientStateProviderStatus Status
    {
        get
        {
            if (LastError is not null)
            {
                return ClientStateProviderStatus.Disconnected;
            }

            try
            {
                return _source.Status;
            }
            catch (Exception)
            {
                return ClientStateProviderStatus.Disconnected;
            }
        }
    }

    public async ValueTask<TSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _lastError, null);
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception);
            CountFailure();
            return null;
        }
    }

    private void CountFailure()
    {
        long current;
        long next;
        do
        {
            current = Interlocked.Read(ref _failureCount);
            next = Math.Min(current + 1, MaximumCountedFailures);
        }
        while (Interlocked.CompareExchange(ref _failureCount, next, current) != current);
    }
}
