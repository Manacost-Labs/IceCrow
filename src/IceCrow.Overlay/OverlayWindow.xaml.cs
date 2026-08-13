using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed partial class OverlayWindow : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const string InteractiveElementTag = "IceCrowInteractive";

    private HwndSource? _windowSource;
    private nint _windowHandle;
    private OverlayInteractionMode _interactionMode = OverlayInteractionMode.ClickThrough;
    private OverlayInteractionModifier _configuredModifier = OverlayInteractionModifier.Alt;
    private int? _hoveredPlayerId;
    private int? _pinnedPlayerId;

    public OverlayWindow()
    {
        InitializeComponent();
        SetConfiguredModifier(_configuredModifier);
    }

    public event EventHandler<OverlayInteractionModifierChangedEventArgs>?
        InteractionModifierRequested;

    public OverlayInteractionMode InteractionMode => _interactionMode;

    public void ApplyState(OverlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dispatcher.VerifyAccess();

        if (!state.IsVisible)
        {
            FailSafeToClickThrough();
            Hide();
            return;
        }

        Left = state.Bounds.Left;
        Top = state.Bounds.Top;
        Width = state.Bounds.Width;
        Height = state.Bounds.Height;
        ConnectionText.Text = state.IsConnected ? "Hearthstone Connected" : string.Empty;
        SizeText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{state.NativeWidth} × {state.NativeHeight}");

        if (!IsVisible)
        {
            Show();
        }
    }

    public void ApplyBattlegroundsState(
        BattlegroundsState state,
        OpponentMemory memory,
        LobbyTimeline? timeline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(memory);
        Dispatcher.VerifyAccess();

        var tiles = OpponentLobbyTileViewState.Create(state, memory, timeline);
        LobbyTiles.ItemsSource = tiles;
        LobbyTiles.Visibility = state.IsActive && state.Lobby.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshDetailPanel(HoverOpponentPanel, _hoveredPlayerId, tiles, clearId: () =>
        {
            _hoveredPlayerId = null;
        });
        RefreshDetailPanel(PinnedOpponentPanel, _pinnedPlayerId, tiles, clearId: () =>
        {
            _pinnedPlayerId = null;
        });
    }

    public void SetInteractionMode(OverlayInteractionMode mode)
    {
        Dispatcher.VerifyAccess();
        if (mode == _interactionMode)
        {
            UpdateInteractionVisuals();
            return;
        }

        try
        {
            if (_windowHandle != nint.Zero)
            {
                NativeWindowStyles.ApplyOverlayStyles(
                    _windowHandle,
                    isClickThrough: mode == OverlayInteractionMode.ClickThrough);
            }

            _interactionMode = mode;
            UpdateInteractionVisuals();
        }
        catch
        {
            FailSafeToClickThrough();
            throw;
        }
    }

    public void SetConfiguredModifier(OverlayInteractionModifier modifier)
    {
        Dispatcher.VerifyAccess();
        _configuredModifier = modifier;
        var displayName = GetModifierDisplayName(modifier);
        InteractionHintText.Text = $"{displayName} held · interactive";
        ConfiguredModifierText.Text = $"Hold {displayName} to interact";
    }

    public void FailSafeToClickThrough()
    {
        Dispatcher.VerifyAccess();
        _interactionMode = OverlayInteractionMode.ClickThrough;
        Mouse.Capture(null);
        UpdateInteractionVisuals();

        if (_windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            NativeWindowStyles.ApplyOverlayStyles(_windowHandle, isClickThrough: true);
        }
        catch
        {
            if (IsVisible)
            {
                Hide();
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        NativeWindowStyles.ApplyOverlayStyles(_windowHandle, isClickThrough: true);

        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowProcedure);
    }

    protected override void OnClosed(EventArgs e)
    {
        FailSafeToClickThrough();
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        _windowHandle = nint.Zero;
        base.OnClosed(e);
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        _ = windowHandle;
        _ = wordParameter;
        switch (message)
        {
            case WmMouseActivate:
                handled = true;
                return new nint(MaNoActivate);
            case WmNcHitTest:
                handled = true;
                return _interactionMode == OverlayInteractionMode.Interactive &&
                       IsInteractiveElementAt(longParameter)
                    ? new nint(HtClient)
                    : new nint(HtTransparent);
            default:
                return nint.Zero;
        }
    }

    private bool IsInteractiveElementAt(nint screenCoordinates)
    {
        var packed = screenCoordinates.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
        var windowPoint = PointFromScreen(screenPoint);
        var current = InputHitTest(windowPoint) as DependencyObject;

        while (current is not null)
        {
            if (current is FrameworkElement { Tag: InteractiveElementTag })
            {
                return true;
            }

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnOpponentTileMouseEnter(object sender, MouseEventArgs eventArgs)
    {
        _ = eventArgs;
        if (_interactionMode != OverlayInteractionMode.Interactive ||
            sender is not FrameworkElement { DataContext: OpponentLobbyTileViewState tile })
        {
            return;
        }

        _hoveredPlayerId = tile.PlayerId;
        HoverOpponentPanel.DataContext = tile;
        HoverOpponentPanel.Visibility = Visibility.Visible;
    }

    private void OnOpponentTileMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _hoveredPlayerId = null;
        HoverOpponentPanel.DataContext = null;
        HoverOpponentPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOpponentTileClicked(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_interactionMode != OverlayInteractionMode.Interactive ||
            sender is not FrameworkElement { DataContext: OpponentLobbyTileViewState tile })
        {
            return;
        }

        _pinnedPlayerId = tile.PlayerId;
        PinnedOpponentPanel.DataContext = tile;
        PinnedOpponentPanel.Visibility = Visibility.Visible;
        eventArgs.Handled = true;
    }

    private void OnClosePinnedClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _pinnedPlayerId = null;
        PinnedOpponentPanel.DataContext = null;
        PinnedOpponentPanel.Visibility = Visibility.Collapsed;
        eventArgs.Handled = true;
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        eventArgs.Handled = true;
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        SettingsPanel.Visibility = Visibility.Collapsed;
        eventArgs.Handled = true;
    }

    private void OnModifierClicked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: string modifierName } ||
            !Enum.TryParse<OverlayInteractionModifier>(
                modifierName,
                ignoreCase: true,
                out var modifier))
        {
            return;
        }

        SetConfiguredModifier(modifier);
        InteractionModifierRequested?.Invoke(
            this,
            new OverlayInteractionModifierChangedEventArgs(modifier));
        eventArgs.Handled = true;
    }

    private void UpdateInteractionVisuals()
    {
        var interactive = _interactionMode == OverlayInteractionMode.Interactive;
        InteractionToolbar.Visibility = interactive
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (interactive)
        {
            return;
        }

        SettingsPanel.Visibility = Visibility.Collapsed;
        HoverOpponentPanel.Visibility = Visibility.Collapsed;
        HoverOpponentPanel.DataContext = null;
        _hoveredPlayerId = null;
    }

    private static void RefreshDetailPanel(
        FrameworkElement panel,
        int? playerId,
        IReadOnlyList<OpponentLobbyTileViewState> tiles,
        Action clearId)
    {
        if (playerId is not int id)
        {
            return;
        }

        var tile = tiles.FirstOrDefault(candidate => candidate.PlayerId == id);
        if (tile is null)
        {
            panel.DataContext = null;
            panel.Visibility = Visibility.Collapsed;
            clearId();
            return;
        }

        panel.DataContext = tile;
    }

    private static string GetModifierDisplayName(OverlayInteractionModifier modifier) =>
        modifier switch
        {
            OverlayInteractionModifier.Alt => "ALT",
            OverlayInteractionModifier.Control => "CTRL",
            OverlayInteractionModifier.Shift => "SHIFT",
            _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null),
        };
}
