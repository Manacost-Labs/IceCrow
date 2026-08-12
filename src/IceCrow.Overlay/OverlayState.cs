using IceCrow.Platform.Windows;

namespace IceCrow.Overlay;

public sealed record OverlayState(
    bool IsConnected,
    bool IsVisible,
    OverlayLogicalBounds Bounds,
    int NativeWidth,
    int NativeHeight)
{
    private const double DefaultDpi = 96;

    public static OverlayState Disconnected { get; } = new(
        IsConnected: false,
        IsVisible: false,
        Bounds: default,
        NativeWidth: 0,
        NativeHeight: 0);

    public static OverlayState FromWindowInfo(HearthstoneWindowInfo windowInfo)
    {
        ArgumentNullException.ThrowIfNull(windowInfo);

        var dpiScale = windowInfo.Dpi / DefaultDpi;
        var logicalBounds = OverlayBoundsCalculator.ToLogicalBounds(
            windowInfo.ClientBounds,
            dpiScale,
            dpiScale);
        var shouldBeVisible = windowInfo.IsVisible &&
                              !windowInfo.IsMinimized &&
                              windowInfo.IsForeground &&
                              !logicalBounds.IsEmpty;

        return new OverlayState(
            IsConnected: true,
            IsVisible: shouldBeVisible,
            Bounds: logicalBounds,
            NativeWidth: windowInfo.ClientBounds.Width,
            NativeHeight: windowInfo.ClientBounds.Height);
    }
}
