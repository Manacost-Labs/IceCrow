using System.Windows.Threading;
using IceCrow.Overlay;
using IceCrow.Hearthstone.Data;
using IceCrow.Presentation;
using IceCrow.Tracking;

namespace IceCrow.App;

internal sealed class LiveOverlayPresenter : IDisposable
{
    private readonly object _gate = new();
    private readonly OverlayHost _overlayHost;
    private readonly Dispatcher _dispatcher;
    private readonly ICardDatabase? _cardDatabase;
    private TrackingSnapshot? _pending;
    private bool _dispatchScheduled;
    private bool _disposed;

    public LiveOverlayPresenter(
        OverlayHost overlayHost,
        Dispatcher dispatcher,
        ICardDatabase? cardDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(overlayHost);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _overlayHost = overlayHost;
        _dispatcher = dispatcher;
        _cardDatabase = cardDatabase;
    }

    public void Publish(TrackingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = snapshot;
            if (_dispatchScheduled)
            {
                return;
            }

            _dispatchScheduled = true;
        }

        _ = _dispatcher.BeginInvoke(DispatcherPriority.Render, DrainLatest);
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        lock (_gate)
        {
            _disposed = true;
            _pending = null;
            _dispatchScheduled = false;
        }
    }

    private void DrainLatest()
    {
        TrackingSnapshot? snapshot;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            snapshot = _pending;
            _pending = null;
            _dispatchScheduled = false;
        }

        if (snapshot is null)
        {
            return;
        }

        _overlayHost.ApplyViewState(BattlegroundsOverlayViewStateFactory.Create(snapshot, _cardDatabase));
    }
}
