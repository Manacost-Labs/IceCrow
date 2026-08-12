using System.ComponentModel;
using System.Diagnostics;

namespace IceCrow.Platform.Windows;

public sealed class HearthstoneWindowLocator
{
    private const string HearthstoneProcessName = "Hearthstone";
    private const string UnityWindowClass = "UnityWndClass";
    private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(1);

    private nint _cachedWindowHandle;
    private uint _cachedProcessId;
    private DateTimeOffset _nextSearchAt;

    public bool TryLocate(out HearthstoneWindowInfo windowInfo)
    {
        if (TryReadCachedWindow(out windowInfo))
        {
            return true;
        }

        InvalidateCachedWindow();

        var now = DateTimeOffset.UtcNow;
        if (now < _nextSearchAt)
        {
            windowInfo = null!;
            return false;
        }

        _nextSearchAt = now + SearchInterval;

        var candidate = NativeMethods.FindWindow(UnityWindowClass, null);
        while (candidate != nint.Zero)
        {
            if (TryReadCandidate(candidate, out windowInfo))
            {
                _cachedWindowHandle = windowInfo.Handle;
                _cachedProcessId = windowInfo.ProcessId;
                return true;
            }

            candidate = NativeMethods.FindWindowEx(nint.Zero, candidate, UnityWindowClass, null);
        }

        windowInfo = null!;
        return false;
    }

    public void Invalidate(nint windowHandle)
    {
        if (_cachedWindowHandle == windowHandle)
        {
            InvalidateCachedWindow();
        }
    }

    internal static bool IsWindowAlive(nint windowHandle, uint expectedProcessId)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId == expectedProcessId && IsHearthstoneProcess(processId);
    }

    private bool TryReadCachedWindow(out HearthstoneWindowInfo windowInfo)
    {
        if (!IsWindowAlive(_cachedWindowHandle, _cachedProcessId))
        {
            windowInfo = null!;
            return false;
        }

        return HearthstoneWindowInfoReader.TryRead(_cachedWindowHandle, _cachedProcessId, out windowInfo);
    }

    private static bool TryReadCandidate(nint windowHandle, out HearthstoneWindowInfo windowInfo)
    {
        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        if (!IsHearthstoneProcess(processId))
        {
            windowInfo = null!;
            return false;
        }

        return HearthstoneWindowInfoReader.TryRead(windowHandle, processId, out windowInfo);
    }

    private static bool IsHearthstoneProcess(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return !process.HasExited &&
                   string.Equals(process.ProcessName, HearthstoneProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private void InvalidateCachedWindow()
    {
        _cachedWindowHandle = nint.Zero;
        _cachedProcessId = 0;
        _nextSearchAt = DateTimeOffset.MinValue;
    }
}
