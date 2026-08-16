using System.Text;

namespace IceCrow.Hearthstone.Logs.Tests;

[Collection(PowerLogTailerTestsGroup.Name)]
public sealed class PowerLogTailerDiagnosticsTests
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task AppendOnlyGrowthRecordsNoFullReread()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await File.WriteAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - first"),
                LogEncoding);
            _ = await tailer.Lines.ReadAsync(cancellation.Token);

            await File.AppendAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - second"),
                LogEncoding,
                cancellation.Token);
            _ = await tailer.Lines.ReadAsync(cancellation.Token);

            Assert.Equal(0, tailer.Diagnostics.FullRereadCount);
            Assert.Null(tailer.Diagnostics.LastResetReason);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task TruncatingRewriteRecordsExactlyOneRereadWithReasonAndOffset()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await File.WriteAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - old-one") +
                Line("GameState.DebugPrintGame() - old-two"),
                LogEncoding);
            _ = await tailer.Lines.ReadAsync(cancellation.Token);
            _ = await tailer.Lines.ReadAsync(cancellation.Token);
            var consumedOffset = tailer.Checkpoint.ByteOffset;

            await File.WriteAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - rewritten"),
                LogEncoding,
                cancellation.Token);
            _ = await tailer.Lines.ReadAsync(cancellation.Token);

            var diagnostics = tailer.Diagnostics;
            Assert.Equal(1, diagnostics.FullRereadCount);
            Assert.NotNull(diagnostics.LastResetReason);
            Assert.NotNull(diagnostics.LastResetAt);
            Assert.Equal(consumedOffset, diagnostics.LastResetOffset);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task RotationToANewDirectoryDoesNotCountAsAFullReread()
    {
        using var directory = new TemporaryLogDirectory();
        var firstDirectory = directory.GetPath("session-1");
        Directory.CreateDirectory(firstDirectory);
        var firstLog = Path.Combine(firstDirectory, "Power.log");
        await File.WriteAllTextAsync(
            firstLog,
            Line("GameState.DebugPrintGame() - old-session"),
            LogEncoding);
        File.SetLastWriteTimeUtc(firstLog, DateTime.UtcNow.AddSeconds(-5));

        var (tailer, cancellation, runTask) = StartTailer(directory.Path);
        try
        {
            _ = await tailer.Lines.ReadAsync(cancellation.Token);

            var secondDirectory = directory.GetPath("session-2");
            Directory.CreateDirectory(secondDirectory);
            var secondLog = Path.Combine(secondDirectory, "Power.log");
            await File.WriteAllTextAsync(
                secondLog,
                Line("GameState.DebugPrintGame() - new-session"),
                LogEncoding);
            File.SetLastWriteTimeUtc(secondLog, DateTime.UtcNow.AddSeconds(1));
            _ = await tailer.Lines.ReadAsync(cancellation.Token);

            var diagnostics = tailer.Diagnostics;
            Assert.Equal(0, diagnostics.FullRereadCount);
            Assert.Equal(LogResetReason.PathChanged, diagnostics.LastResetReason);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    private static (PowerLogTailer Tailer, CancellationTokenSource Cancellation, Task RunTask)
        StartTailer(string logRoot)
    {
        var locator = new HearthstoneLogLocator(logRoots: [logRoot]);
        var tailer = new PowerLogTailer(locator, recoveryInterval: RecoveryInterval);
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return (tailer, cancellation, tailer.RunAsync(cancellation.Token));
    }

    private static async Task StopTailerAsync(
        CancellationTokenSource cancellation,
        Task runTask)
    {
        cancellation.Cancel();
        await runTask;
        cancellation.Dispose();
    }

    private static string Line(string content)
    {
        return $"D 17:00:00.0000000 {content}\r\n";
    }
}
