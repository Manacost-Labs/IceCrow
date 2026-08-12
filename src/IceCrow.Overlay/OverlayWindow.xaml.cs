using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed partial class OverlayWindow : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var windowHandle = new WindowInteropHelper(this).Handle;
        NativeWindowStyles.ApplyOverlayStyles(windowHandle);

        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowProcedure);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        base.OnClosed(e);
    }

    private static nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        _ = windowHandle;
        _ = wordParameter;
        _ = longParameter;

        switch (message)
        {
            case WmMouseActivate:
                handled = true;
                return new nint(MaNoActivate);
            case WmNcHitTest:
                handled = true;
                return new nint(HtTransparent);
            default:
                return nint.Zero;
        }
    }
}
