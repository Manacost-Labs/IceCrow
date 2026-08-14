using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using IceCrow.Overlay.Controls;
using IceCrow.Platform.Windows;
using IceCrow.Presentation;

namespace IceCrow.Overlay;

public sealed partial class OverlayWindow : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const string InteractiveElementTag = "IceCrowInteractive";

    /// <summary>Player id of the pinned opponent, or zero. Drives the selection marker.</summary>
    public static readonly DependencyProperty PinnedPlayerIdProperty =
        DependencyProperty.Register(
            nameof(PinnedPlayerId),
            typeof(int),
            typeof(OverlayWindow),
            new PropertyMetadata(0));

    private readonly ObservableCollection<OpponentOverlayViewState> _rows = [];
    private readonly OverlayImageCache _imageCache;

    private HwndSource? _windowSource;
    private nint _windowHandle;
    private OverlayInteractionMode _interactionMode = OverlayInteractionMode.ClickThrough;
    private OverlayInteractionModifier _configuredModifier = OverlayInteractionModifier.Alt;
    private OverlayRenderingSettings _renderingSettings = OverlayRenderingSettings.Default;
    private OverlayLayoutMode _layoutMode = OverlayLayoutMode.Regular;
    private BattlegroundsOverlayViewState _viewState = BattlegroundsOverlayViewState.Empty;
    private int? _hoveredPlayerId;

    public OverlayWindow()
        : this(new OverlayRenderDiagnostics())
    {
    }

    public OverlayWindow(OverlayRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
        _imageCache = new OverlayImageCache(diagnostics);

        InitializeComponent();

        LobbyRows.ItemsSource = _rows;
        OverlayVisuals.SetImageCache(this, _imageCache);
        ApplyRenderingSettings(_renderingSettings);
        ApplyLayoutMode(_layoutMode);
        SetConfiguredModifier(_configuredModifier);
    }

    public event EventHandler<OverlayInteractionModifierChangedEventArgs>?
        InteractionModifierRequested;

    public OverlayRenderDiagnostics Diagnostics { get; }

    public OverlayInteractionMode InteractionMode => _interactionMode;

    public OverlayRenderingSettings RenderingSettings => _renderingSettings;

    public OverlayLayoutMode LayoutMode => _layoutMode;

    public int PinnedPlayerId
    {
        get => (int)GetValue(PinnedPlayerIdProperty);
        private set => SetValue(PinnedPlayerIdProperty, value);
    }

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
        ConnectionText.Text = state.IsConnected ? "Hearthstone connected" : string.Empty;
        SizeText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{state.NativeWidth} × {state.NativeHeight}");
        ApplyLayoutMode(OverlayLayout.FromClientWidth(state.Bounds.Width));

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Applies a projected view state. Value-equal updates are dropped, and only
    /// the lobby rows whose value changed are replaced, so a snapshot that moved
    /// no visible information costs nothing in WPF.
    /// </summary>
    public void ApplyViewState(BattlegroundsOverlayViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        Dispatcher.VerifyAccess();

        if (viewState.Equals(_viewState))
        {
            Diagnostics.RecordViewStateSkipped();
            return;
        }

        var previous = _viewState;
        _viewState = viewState;
        Diagnostics.RecordViewStateApplied();

        MergeRows(viewState.Opponents);
        LobbyRows.Visibility = viewState.ShowLobby
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshDetailPanel(HoverOpponentPanel, _hoveredPlayerId, viewState, () => _hoveredPlayerId = null);
        RefreshDetailPanel(PinnedOpponentPanel, NullablePinnedPlayerId, viewState, () => PinnedPlayerId = 0);

        if (_renderingSettings.AllowEventPulse && HasNewTriple(previous, viewState))
        {
            StatusPanel.PulseSignature(Diagnostics);
        }
    }

    public void SetRenderingSettings(OverlayRenderingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Dispatcher.VerifyAccess();
        ApplyRenderingSettings(settings);
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

    private int? NullablePinnedPlayerId => PinnedPlayerId == 0 ? null : PinnedPlayerId;

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

    /// <summary>
    /// Replaces only the rows whose value changed. An eight-player lobby does not
    /// need virtualization, but it must not rebuild every container per snapshot.
    /// </summary>
    private void MergeRows(IReadOnlyList<OpponentOverlayViewState> opponents)
    {
        for (var index = 0; index < opponents.Count; index++)
        {
            if (index >= _rows.Count)
            {
                _rows.Add(opponents[index]);
                Diagnostics.RecordOpponentRowReplaced();
                continue;
            }

            if (!_rows[index].Equals(opponents[index]))
            {
                _rows[index] = opponents[index];
                Diagnostics.RecordOpponentRowReplaced();
            }
        }

        while (_rows.Count > opponents.Count)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private static void RefreshDetailPanel(
        IcePanel panel,
        int? playerId,
        BattlegroundsOverlayViewState viewState,
        Action clearId)
    {
        if (playerId is not int id)
        {
            return;
        }

        var opponent = viewState.Opponents.FirstOrDefault(candidate => candidate.PlayerId == id);
        if (opponent is null)
        {
            HideDetailPanel(panel);
            clearId();
            return;
        }

        panel.DataContext = opponent;
    }

    private void ShowDetailPanel(IcePanel panel, OpponentOverlayViewState opponent)
    {
        panel.DataContext = opponent;
        if (panel.Visibility == Visibility.Visible)
        {
            return;
        }

        panel.Visibility = Visibility.Visible;
        if (_renderingSettings.AllowEntranceAnimation)
        {
            panel.PlayEntrance(Diagnostics);
        }
    }

    private static void HideDetailPanel(IcePanel panel)
    {
        panel.Visibility = Visibility.Collapsed;
        panel.DataContext = null;
    }

    private static bool HasNewTriple(
        BattlegroundsOverlayViewState previous,
        BattlegroundsOverlayViewState current)
    {
        foreach (var opponent in current.Opponents)
        {
            var before = previous.Opponents
                .FirstOrDefault(candidate => candidate.PlayerId == opponent.PlayerId);
            if (before is not null && opponent.Triples > before.Triples)
            {
                return true;
            }
        }

        return false;
    }

    private void OnOpponentRowMouseEnter(object sender, MouseEventArgs eventArgs)
    {
        _ = eventArgs;
        if (_interactionMode != OverlayInteractionMode.Interactive ||
            sender is not FrameworkElement { DataContext: OpponentOverlayViewState opponent })
        {
            return;
        }

        _hoveredPlayerId = opponent.PlayerId;
        ShowDetailPanel(HoverOpponentPanel, opponent);
    }

    private void OnOpponentRowMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _hoveredPlayerId = null;
        HideDetailPanel(HoverOpponentPanel);
    }

    private void OnOpponentRowClicked(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_interactionMode != OverlayInteractionMode.Interactive ||
            sender is not FrameworkElement { DataContext: OpponentOverlayViewState opponent })
        {
            return;
        }

        PinnedPlayerId = opponent.PlayerId;
        ShowDetailPanel(PinnedOpponentPanel, opponent);
        eventArgs.Handled = true;
    }

    private void OnClosePinnedClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        PinnedPlayerId = 0;
        HideDetailPanel(PinnedOpponentPanel);
        eventArgs.Handled = true;
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Visible;
            if (_renderingSettings.AllowEntranceAnimation)
            {
                SettingsPanel.PlayEntrance(Diagnostics);
            }
        }

        eventArgs.Handled = true;
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        SettingsPanel.Visibility = Visibility.Collapsed;
        eventArgs.Handled = true;
    }

    private void OnPerformanceModeClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        ApplyRenderingSettings(_renderingSettings.PerformanceMode
            ? OverlayRenderingSettings.Default
            : OverlayRenderingSettings.Performance);
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

    private void ApplyRenderingSettings(OverlayRenderingSettings settings)
    {
        _renderingSettings = settings;
        OverlayVisuals.SetEffectsEnabled(this, !settings.PerformanceMode);

        StatusPanel.HasElevation = settings.AllowPanelShadow;
        HoverOpponentPanel.HasElevation = settings.AllowPanelShadow;
        PinnedOpponentPanel.HasElevation = settings.AllowPanelShadow;
        SettingsPanel.HasElevation = settings.AllowPanelShadow;
        InteractionToolbar.HasElevation = settings.AllowPanelShadow;

        PerformanceModeButton.Content = settings.PerformanceMode
            ? "Performance mode: on"
            : "Performance mode: off";
    }

    private void ApplyLayoutMode(OverlayLayoutMode mode)
    {
        _layoutMode = mode;
        OverlayVisuals.SetLayoutMode(this, mode);

        var detailWidth = OverlayLayout.DetailPanelWidth(mode);
        HoverOpponentPanel.Width = detailWidth;
        PinnedOpponentPanel.Width = detailWidth;
        HoverOpponentPanel.Margin = new Thickness(
            OverlayLayout.OpponentRowWidth(mode) + 32,
            20,
            0,
            0);
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
        HideDetailPanel(HoverOpponentPanel);
        _hoveredPlayerId = null;
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
