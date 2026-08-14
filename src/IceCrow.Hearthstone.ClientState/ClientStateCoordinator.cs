using System.IO;

namespace IceCrow.Hearthstone.ClientState;

public sealed class ClientStateCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan MinimumPollingInterval = TimeSpan.FromMilliseconds(25);
    public static readonly TimeSpan MaximumPollingInterval = TimeSpan.FromMinutes(1);

    private readonly IClientStateProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientStateSnapshot? _lastSnapshot;
    private bool _disposed;

    public ClientStateCoordinator(
        IClientStateProvider provider,
        TimeProvider? timeProvider = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ClientStateSnapshot? LastSnapshot => _lastSnapshot;

    public Exception? LastProviderError { get; private set; }

    public async ValueTask<ClientStateChange?> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = await ReadSafelyAsync(cancellationToken).ConfigureAwait(false);
            if (_lastSnapshot?.SemanticallyEquals(current) == true)
            {
                return null;
            }

            var change = ClientStateChange.Between(_lastSnapshot, current);
            _lastSnapshot = current;
            return change;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunAsync(
        TimeSpan pollingInterval,
        Func<ClientStateChange, CancellationToken, ValueTask> publishAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishAsync);
        if (pollingInterval < MinimumPollingInterval || pollingInterval > MaximumPollingInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                $"Polling interval must be between {MinimumPollingInterval} and {MaximumPollingInterval}.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (change is not null)
            {
                await publishAsync(change, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(pollingInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<ClientStateSnapshot> ReadSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            var unsupportedCapabilities = snapshot.AvailableCapabilities & ~_provider.Capabilities;
            if (unsupportedCapabilities != ClientStateCapabilities.None)
            {
                throw new InvalidDataException(
                    $"Provider returned capabilities it did not declare: {unsupportedCapabilities}.");
            }

            LastProviderError = null;
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LastProviderError = exception;
            return ClientStateSnapshot.WithoutClientState(
                _timeProvider.GetUtcNow(),
                ClientStateProviderStatus.Disconnected);
        }
    }
}
