using System.Windows;
using System.Windows.Threading;

namespace IceCrow.Overlay.Tests;

/// <summary>
/// Runs WPF assertions on one shared STA thread.
/// </summary>
/// <remarks>
/// WPF requires STA and allows a single <see cref="Application"/> per process,
/// so every test body has to share one UI thread. A dedicated dispatcher thread
/// keeps the test project on the BCL instead of adding an STA runner package.
/// </remarks>
internal static class StaTestRunner
{
    private static readonly Lazy<Dispatcher> UiDispatcher =
        new(StartUiThread, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UiDispatcher.Value.Invoke(action);
    }

    private static Dispatcher StartUiThread()
    {
        Dispatcher? dispatcher = null;
        using var started = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            started.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "IceCrow overlay test UI",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)), "The STA test thread did not start.");
        return dispatcher!;
    }
}
