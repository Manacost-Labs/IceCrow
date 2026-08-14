using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceCrow.Platform.Windows;

public sealed class HearthstoneWindowTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;

    private readonly List<nint> _hookHandles = [];
    private readonly WinEventCallback _callback;
    private readonly GCHandle _callbackHandle;
    private readonly int _ownerThreadId;
    private HearthstoneWindowInfo _current;
    private bool _disposed;
    private bool _unavailableRaised;

    public HearthstoneWindowTracker(HearthstoneWindowInfo initialWindowInfo)
    {
        ArgumentNullException.ThrowIfNull(initialWindowInfo);

        if (!HearthstoneWindowLocator.IsWindowAlive(initialWindowInfo.Handle, initialWindowInfo.ProcessId))
        {
            throw new ArgumentException("The Hearthstone window is no longer valid.", nameof(initialWindowInfo));
        }

        _current = initialWindowInfo;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _callback = OnWinEvent;
        _callbackHandle = GCHandle.Alloc(_callback);

        try
        {
            InstallHook(EventObjectLocationChange, EventObjectLocationChange, initialWindowInfo.ProcessId);
            InstallHook(EventSystemMinimizeStart, EventSystemMinimizeEnd, initialWindowInfo.ProcessId);
            InstallHook(EventObjectDestroy, EventObjectDestroy, initialWindowInfo.ProcessId);
            InstallHook(EventSystemForeground, EventSystemForeground, 0);
        }
        catch
        {
            DisposeHooks(throwOnFailure: false);
            _callbackHandle.Free();
            throw;
        }
    }

    public event EventHandler<HearthstoneWindowInfo>? WindowChanged;

    public event EventHandler? WindowUnavailable;

    public HearthstoneWindowInfo Current => _current;

    public bool IsWindowAlive =>
        !_disposed && HearthstoneWindowLocator.IsWindowAlive(_current.Handle, _current.ProcessId);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("WinEvent hooks must be removed on the thread that installed them.");
        }

        _disposed = true;
        DisposeHooks(throwOnFailure: true);
        _callbackHandle.Free();
    }

    private void InstallHook(uint eventMinimum, uint eventMaximum, uint processId)
    {
        var hookHandle = NativeMethods.SetWinEventHook(
            eventMinimum,
            eventMaximum,
            nint.Zero,
            _callback,
            processId,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        if (hookHandle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install a Hearthstone WinEvent hook.");
        }

        _hookHandles.Add(hookHandle);
    }

    private void OnWinEvent(
        nint hookHandle,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTimeMilliseconds)
    {
        _ = hookHandle;
        _ = eventThreadId;
        _ = eventTimeMilliseconds;

        try
        {
            ProcessWinEvent(eventType, windowHandle, objectId, childId);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TryRaiseWindowUnavailable();
        }
    }

    private void ProcessWinEvent(
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId)
    {
        if (_disposed)
        {
            return;
        }

        if (eventType == EventSystemForeground)
        {
            RefreshWindowInfo();
            return;
        }

        if (windowHandle != _current.Handle || objectId != ObjectIdWindow || childId != ChildIdSelf)
        {
            return;
        }

        if (eventType == EventObjectDestroy)
        {
            RaiseWindowUnavailable();
            return;
        }

        RefreshWindowInfo();
    }

    private void TryRaiseWindowUnavailable()
    {
        try
        {
            RaiseWindowUnavailable();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void RefreshWindowInfo()
    {
        if (!HearthstoneWindowLocator.IsWindowAlive(_current.Handle, _current.ProcessId) ||
            !HearthstoneWindowInfoReader.TryRead(_current.Handle, _current.ProcessId, out var updatedWindowInfo))
        {
            RaiseWindowUnavailable();
            return;
        }

        _current = updatedWindowInfo;
        WindowChanged?.Invoke(this, updatedWindowInfo);
    }

    private void RaiseWindowUnavailable()
    {
        if (_unavailableRaised)
        {
            return;
        }

        _unavailableRaised = true;
        WindowUnavailable?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeHooks(bool throwOnFailure)
    {
        Win32Exception? failure = null;

        for (var index = _hookHandles.Count - 1; index >= 0; index--)
        {
            if (!NativeMethods.UnhookWinEvent(_hookHandles[index]) && throwOnFailure && failure is null)
            {
                failure = new Win32Exception(Marshal.GetLastWin32Error(), "Could not remove a Hearthstone WinEvent hook.");
            }
        }

        _hookHandles.Clear();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
