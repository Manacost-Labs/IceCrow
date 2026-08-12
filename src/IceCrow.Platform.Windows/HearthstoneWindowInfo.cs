using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IceCrow.Platform.Windows;

public readonly record struct NativeClientBounds(int Left, int Top, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record HearthstoneWindowInfo(
    nint Handle,
    uint ProcessId,
    uint ThreadId,
    NativeClientBounds ClientBounds,
    uint Dpi,
    bool IsMinimized,
    bool IsVisible,
    bool IsForeground);

internal static class HearthstoneWindowInfoReader
{
    private const uint DefaultDpi = 96;

    internal static bool TryRead(nint windowHandle, uint expectedProcessId, out HearthstoneWindowInfo windowInfo)
    {
        windowInfo = null!;

        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        if (threadId == 0 || processId == 0 || (expectedProcessId != 0 && processId != expectedProcessId))
        {
            return false;
        }

        if (!NativeMethods.GetClientRect(windowHandle, out var clientRectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the Hearthstone client rectangle.");
        }

        var clientOrigin = new NativePoint
        {
            X = clientRectangle.Left,
            Y = clientRectangle.Top,
        };

        if (!NativeMethods.ClientToScreen(windowHandle, ref clientOrigin))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not convert Hearthstone client coordinates to screen coordinates.");
        }

        var dpi = NativeMethods.GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            dpi = DefaultDpi;
        }

        var clientBounds = new NativeClientBounds(
            clientOrigin.X,
            clientOrigin.Y,
            clientRectangle.Right - clientRectangle.Left,
            clientRectangle.Bottom - clientRectangle.Top);

        windowInfo = new HearthstoneWindowInfo(
            windowHandle,
            processId,
            threadId,
            clientBounds,
            dpi,
            NativeMethods.IsIconic(windowHandle),
            NativeMethods.IsWindowVisible(windowHandle),
            NativeMethods.GetForegroundWindow() == windowHandle);

        return true;
    }
}
