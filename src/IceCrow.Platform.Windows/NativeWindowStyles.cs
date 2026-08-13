using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IceCrow.Platform.Windows;

public static class NativeWindowStyles
{
    public const long WsExTransparent = 0x00000020L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static void ApplyOverlayStyles(
        nint windowHandle,
        bool isClickThrough = true)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid overlay window handle is required.", nameof(windowHandle));
        }

        Marshal.SetLastPInvokeError(0);
        var currentStyles = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle);
        var getLastError = Marshal.GetLastPInvokeError();
        if (currentStyles == nint.Zero && getLastError != 0)
        {
            throw new Win32Exception(getLastError, "Could not read overlay extended window styles.");
        }

        var desiredStyleValue = CalculateOverlayExtendedStyles(
            currentStyles.ToInt64(),
            isClickThrough);
        var desiredStyles = new nint(desiredStyleValue);
        if (desiredStyles != currentStyles)
        {
            Marshal.SetLastPInvokeError(0);
            var previousStyles = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle, desiredStyles);
            var setLastError = Marshal.GetLastPInvokeError();
            if (previousStyles == nint.Zero && setLastError != 0)
            {
                throw new Win32Exception(setLastError, "Could not apply overlay extended window styles.");
            }
        }

        if (!NativeMethods.SetWindowPos(
                windowHandle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not refresh overlay window styles.");
        }
    }

    public static long CalculateOverlayExtendedStyles(
        long currentStyles,
        bool isClickThrough)
    {
        var desiredStyles = currentStyles | WsExToolWindow | WsExNoActivate;
        return isClickThrough
            ? desiredStyles | WsExTransparent
            : desiredStyles & ~WsExTransparent;
    }
}
