using System.Globalization;
using System.Windows;
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
    private const string InteractiveTileTag = "IceCrowOpponentTile";

    private HwndSource? _windowSource;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void ApplyState(OverlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dispatcher.VerifyAccess();

        if (!state.IsVisible)
        {
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

        LobbyTiles.ItemsSource = OpponentLobbyTileViewState.Create(state, memory, timeline);
        LobbyTiles.Visibility = state.IsActive && state.Lobby.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var windowHandle = new WindowInteropHelper(this).Handle;
        NativeWindowStyles.ApplyOverlayStyles(windowHandle, isClickThrough: false);

        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowProcedure);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
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
                return IsInteractiveTileAt(longParameter)
                    ? new nint(HtClient)
                    : new nint(HtTransparent);
            default:
                return nint.Zero;
        }
    }

    private bool IsInteractiveTileAt(nint screenCoordinates)
    {
        var packed = screenCoordinates.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
        var windowPoint = PointFromScreen(screenPoint);
        var current = InputHitTest(windowPoint) as DependencyObject;

        while (current is not null)
        {
            if (current is FrameworkElement { Tag: InteractiveTileTag })
            {
                return true;
            }

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}
