using System.Windows.Threading;
using IceCrow.Hearthstone.Data;
using IceCrow.Overlay;
using IceCrow.Tracking;

namespace IceCrow.App.Runtime;

internal sealed class PresentationRuntime : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly OverlayHost _overlayHost = new();
    private readonly LiveOverlayPresenter _presenter;
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeRequested;
    private int _resourcesDisposed;

    public PresentationRuntime(Dispatcher dispatcher, ICardDatabase cardDatabase)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(cardDatabase);
        _dispatcher = dispatcher;
        _presenter = new LiveOverlayPresenter(_overlayHost, dispatcher, cardDatabase);
    }

    /// <summary>Overlay rendering counters for the developer diagnostics view.</summary>
    public OverlayRenderDiagnostics OverlayDiagnostics => _overlayHost.Diagnostics;

    public void Start() => _overlayHost.Start();

    public void Publish(TrackingSnapshot snapshot) => _presenter.Publish(snapshot);

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _resourcesDisposed) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (_dispatcher.CheckAccess())
        {
            DisposeOnDispatcher();
            return ValueTask.CompletedTask;
        }

        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
        {
            try
            {
                var operation = _dispatcher.BeginInvoke(DispatcherPriority.Send, DisposeOnDispatcher);
                _ = ObserveDispatcherOperationAsync(operation.Task);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
            {
                _disposed.TrySetException(exception);
            }
        }

        return new ValueTask(_disposed.Task);
    }

    public void DisposeSynchronouslyForExit()
    {
        _dispatcher.VerifyAccess();
        DisposeOnDispatcher();
    }

    private void DisposeOnDispatcher()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            _disposed.TrySetResult();
            return;
        }

        try
        {
            _presenter.Dispose();
            _overlayHost.Dispose();
            _disposed.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposed.TrySetException(exception);
            throw;
        }
    }

    private async Task ObserveDispatcherOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
        {
            _disposed.TrySetException(exception);
        }
    }
}
