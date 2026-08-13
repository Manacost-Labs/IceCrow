using System.Diagnostics;
using System.Windows.Threading;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed class OverlayHost : IDisposable
{
    private static readonly TimeSpan LifecycleCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ModifierCheckInterval = TimeSpan.FromMilliseconds(33);

    private readonly HearthstoneWindowLocator _windowLocator = new();
    private readonly OverlayWindow _overlayWindow = new();
    private readonly OverlayInteractionStateMachine _interactionStateMachine = new();
    private readonly DispatcherTimer _lifecycleTimer;
    private readonly DispatcherTimer _modifierTimer;
    private HearthstoneWindowTracker? _windowTracker;
    private OverlayInteractionModifier _interactionModifier = OverlayInteractionModifier.Alt;
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
        _modifierTimer = new DispatcherTimer(
            ModifierCheckInterval,
            DispatcherPriority.Input,
            OnModifierTimerTick,
            _overlayWindow.Dispatcher);
        _modifierTimer.Stop();
        _overlayWindow.InteractionModifierRequested += OnInteractionModifierRequested;
        _overlayWindow.SetConfiguredModifier(_interactionModifier);
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
        _modifierTimer.Start();
    }

    public void ApplyBattlegroundsState(
        BattlegroundsState state,
        OpponentMemory memory,
        LobbyTimeline? timeline = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _overlayWindow.Dispatcher.VerifyAccess();
        _overlayWindow.ApplyBattlegroundsState(state, memory, timeline);
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
        _modifierTimer.Stop();
        _overlayWindow.InteractionModifierRequested -= OnInteractionModifierRequested;

        try
        {
            FailSafeInteraction();
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

    private void OnModifierTimerTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        try
        {
            var modifierHeld = State.IsVisible &&
                               ModifierKeyStateReader.IsHeld(_interactionModifier);
            var transition = _interactionStateMachine.Update(
                modifierHeld,
                overlayAvailable: State.IsVisible);
            ApplyInteractionTransition(transition);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FailSafeInteraction();
        }
    }

    private void OnInteractionModifierRequested(
        object? sender,
        OverlayInteractionModifierChangedEventArgs eventArgs)
    {
        _ = sender;
        FailSafeInteraction();
        _interactionModifier = eventArgs.Modifier;
        _overlayWindow.SetConfiguredModifier(_interactionModifier);
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
        if (!State.IsVisible)
        {
            FailSafeInteraction();
        }

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
        FailSafeInteraction();
        _overlayWindow.ApplyState(State);
    }

    private void ApplyInteractionTransition(OverlayInteractionTransition transition)
    {
        if (!transition.HasChanged)
        {
            return;
        }

        _overlayWindow.SetInteractionMode(transition.Current);
    }

    private void FailSafeInteraction()
    {
        _ = _interactionStateMachine.FailSafe();
        _overlayWindow.FailSafeToClickThrough();
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
