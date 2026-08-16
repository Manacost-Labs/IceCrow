using System.Threading.Channels;
using IceCrow.Recording;

namespace IceCrow.App.Runtime;

/// <summary>
/// Bounded, single-reader queue that moves completed match captures off the
/// live observer callback and processes them sequentially. Owns only queue
/// mechanics and lifecycle: the processor callback owns save semantics and
/// must swallow every non-cancellation exception itself. Shutdown completes
/// the queue, drains for a bounded grace period, cancels the in-flight save,
/// and as a last resort abandons a save that ignores cancellation rather
/// than blocking WPF shutdown.
/// </summary>
internal sealed class CapturePersistenceQueue : IAsyncDisposable
{
    private readonly Channel<RecordedMatch> _pending;
    private readonly Func<RecordedMatch, CancellationToken, Task> _process;
    private readonly Action<int> _onAbandoned;
    private readonly TimeSpan _shutdownGracePeriod;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _worker = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    public CapturePersistenceQueue(
        int capacity,
        Func<RecordedMatch, CancellationToken, Task> process,
        Action<int> onAbandoned,
        TimeSpan shutdownGracePeriod,
        TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(onAbandoned);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _process = process;
        _onAbandoned = onAbandoned;
        _shutdownGracePeriod = shutdownGracePeriod;
        _timeProvider = timeProvider;
        _pending = Channel.CreateBounded<RecordedMatch>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException(
                "The capture persistence queue can only be started once.");
        }

        _started = true;
        _worker = RunAsync();
    }

    /// <summary>Non-blocking enqueue from the observer path; false when full.</summary>
    public bool TryEnqueue(RecordedMatch match) => _pending.Writer.TryWrite(match);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(_shutdownGracePeriod, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _shutdown.Cancel();
            try
            {
                await _worker.WaitAsync(_shutdownGracePeriod, _timeProvider).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The save ignored cancellation. Abandon it rather than block
                // shutdown; observe its eventual fault so it can never surface
                // as an unobserved task exception. The token source stays
                // undisposed because the abandoned worker may still read it.
                _ = _worker.ContinueWith(
                    static task => _ = task.Exception,
                    TaskScheduler.Default);
                return;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var match in _pending.Reader
                               .ReadAllAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                await _process(match, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            ReportAbandoned();
        }
    }

    private void ReportAbandoned()
    {
        var abandoned = 0;
        while (_pending.Reader.TryRead(out _))
        {
            abandoned++;
        }

        if (abandoned > 0)
        {
            _onAbandoned(abandoned);
        }
    }
}
