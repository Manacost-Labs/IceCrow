using System.Windows.Threading;
using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed class OverlayHost : IDisposable
{
    private static readonly TimeSpan LifecycleCheckInterval = TimeSpan.FromSeconds(1);

    private readonly HearthstoneWindowLocator _windowLocator = new();
    private readonly OverlayWindow _overlayWindow = new();
    private readonly DispatcherTimer _lifecycleTimer;
    private HearthstoneWindowTracker? _windowTracker;
    private bool _started;
    private bool _disposed;

    public OverlayHost()
    {
        _lifecycleTimer = new DispatcherTimer(
            LifecycleCheckInterval,
            DispatcherPriority.Background,
            OnLifecycleTimerTick,
            _overlayWindow.Dispatcher);
        _lifecycleTimer.Stop();
    }

    public OverlayState State { get; private set; } = OverlayState.Disconnected;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlayWindow.Dispatcher.VerifyAccess();

        if (_started)
        {
            return;
        }

        _started = true;
        TryConnect();
        _lifecycleTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _overlayWindow.Dispatcher.VerifyAccess();
        _disposed = true;
        _lifecycleTimer.Stop();

        try
        {
            Disconnect();
        }
        finally
        {
            _overlayWindow.Close();
        }
    }

    private void OnLifecycleTimerTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        if (_windowTracker is null)
        {
            TryConnect();
            return;
        }

        if (!_windowTracker.IsWindowAlive)
        {
            Disconnect();
            TryConnect();
        }
    }

    private void TryConnect()
    {
        if (_windowTracker is not null || !_windowLocator.TryLocate(out var windowInfo))
        {
            return;
        }

        _windowTracker = new HearthstoneWindowTracker(windowInfo);
        _windowTracker.WindowChanged += OnHearthstoneWindowChanged;
        _windowTracker.WindowUnavailable += OnHearthstoneWindowUnavailable;
        ApplyWindowInfo(windowInfo);
    }

    private void OnHearthstoneWindowChanged(object? sender, HearthstoneWindowInfo windowInfo)
    {
        _ = sender;
        RunOnDispatcher(() => ApplyWindowInfo(windowInfo));
    }

    private void OnHearthstoneWindowUnavailable(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _overlayWindow.Dispatcher.BeginInvoke(DispatcherPriority.Background, Disconnect);
    }

    private void ApplyWindowInfo(HearthstoneWindowInfo windowInfo)
    {
        State = OverlayState.FromWindowInfo(windowInfo);
        _overlayWindow.ApplyState(State);
    }

    private void Disconnect()
    {
        if (_windowTracker is not null)
        {
            var disconnectedHandle = _windowTracker.Current.Handle;
            _windowTracker.WindowChanged -= OnHearthstoneWindowChanged;
            _windowTracker.WindowUnavailable -= OnHearthstoneWindowUnavailable;
            _windowTracker.Dispose();
            _windowTracker = null;
            _windowLocator.Invalidate(disconnectedHandle);
        }

        State = OverlayState.Disconnected;
        _overlayWindow.ApplyState(State);
    }

    private void RunOnDispatcher(Action action)
    {
        if (_overlayWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _overlayWindow.Dispatcher.BeginInvoke(DispatcherPriority.Render, action);
    }
}
