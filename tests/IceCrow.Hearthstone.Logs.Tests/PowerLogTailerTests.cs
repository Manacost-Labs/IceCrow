using System.Text;
using IceCrow.Hearthstone.Logs;

namespace IceCrow.Hearthstone.Logs.Tests;

public sealed class PowerLogTailerTests
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void RejectsChannelCapacityAboveTheSafetyLimit()
    {
        var locator = new HearthstoneLogLocator(logRoots: [Path.GetTempPath()]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PowerLogTailer(locator, channelCapacity: 257));
    }

    [Fact]
    public async Task ReadsNormalAppendAndFiltersUnrelatedLines()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await File.WriteAllTextAsync(
                powerLog,
                Line("PowerTaskList.DebugPrintPower() - CREATE_ENTITY") +
                Line("AssetLoader.Unrelated() - ignored"),
                LogEncoding);

            var result = await ReadNextAsync(tailer, cancellation.Token);

            Assert.Equal("Power", result.Namespace);
            Assert.StartsWith("PowerTaskList.DebugPrintPower", result.Content, StringComparison.Ordinal);
            Assert.Contains("CREATE_ENTITY", result.OriginalText, StringComparison.Ordinal);
            await Task.Delay(100, cancellation.Token);
            Assert.False(tailer.Lines.TryRead(out _));
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task ReadsTwoAppendedBatchesWithoutRereadingTheFirst()
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
            var first = await ReadNextAsync(tailer, cancellation.Token);

            await File.AppendAllTextAsync(
                powerLog,
                Line("PowerProcessor.EndCurrentTaskList() - second"),
                LogEncoding,
                cancellation.Token);
            var second = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith("first", first.Content, StringComparison.Ordinal);
            Assert.EndsWith("second", second.Content, StringComparison.Ordinal);
            Assert.True(tailer.Checkpoint.ByteOffset > 0);
            await Task.Delay(100, cancellation.Token);
            Assert.False(tailer.Lines.TryRead(out _));
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task HoldsPartialCrLfLineUntilTheLineIsComplete()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var partial = Line("GameState.DebugPrintGame() - partial").TrimEnd('\r', '\n');
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await File.WriteAllTextAsync(powerLog, partial, LogEncoding);
            await Task.Delay(100, cancellation.Token);
            Assert.False(tailer.Lines.TryRead(out _));
            Assert.Equal(0, tailer.Checkpoint.ByteOffset);

            await File.AppendAllTextAsync(
                powerLog,
                "\r\n",
                LogEncoding,
                cancellation.Token);
            var result = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith("partial", result.Content, StringComparison.Ordinal);
            Assert.Equal(new FileInfo(powerLog).Length, tailer.Checkpoint.ByteOffset);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task SwitchesToNewestRotatedLogDirectory()
    {
        using var directory = new TemporaryLogDirectory();
        var firstDirectory = directory.GetPath("session-1");
        Directory.CreateDirectory(firstDirectory);
        var firstLog = System.IO.Path.Combine(firstDirectory, "Power.log");
        await File.WriteAllTextAsync(
            firstLog,
            Line("GameState.DebugPrintGame() - old-session"),
            LogEncoding);
        File.SetLastWriteTimeUtc(firstLog, DateTime.UtcNow.AddSeconds(-5));

        var (tailer, cancellation, runTask) = StartTailer(directory.Path);
        try
        {
            var first = await ReadNextAsync(tailer, cancellation.Token);

            var secondDirectory = directory.GetPath("session-2");
            Directory.CreateDirectory(secondDirectory);
            var secondLog = System.IO.Path.Combine(secondDirectory, "Power.log");
            await File.WriteAllTextAsync(
                secondLog,
                Line("GameState.DebugPrintGame() - new-session"),
                LogEncoding);
            File.SetLastWriteTimeUtc(secondLog, DateTime.UtcNow.AddSeconds(1));

            var second = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith("old-session", first.Content, StringComparison.Ordinal);
            Assert.EndsWith("new-session", second.Content, StringComparison.Ordinal);
            Assert.Equal(secondLog, tailer.Checkpoint.FilePath, ignoreCase: true);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task ResetsOffsetWhenHearthstoneRecreatesTheSameLogPath()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        await File.WriteAllTextAsync(
            powerLog,
            Line("GameState.DebugPrintGame() - old-one") +
            Line("GameState.DebugPrintGame() - old-two"),
            LogEncoding);

        var (tailer, cancellation, runTask) = StartTailer(directory.Path);
        try
        {
            _ = await ReadNextAsync(tailer, cancellation.Token);
            _ = await ReadNextAsync(tailer, cancellation.Token);
            var oldOffset = tailer.Checkpoint.ByteOffset;

            await File.WriteAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - after-restart"),
                LogEncoding,
                cancellation.Token);
            var restarted = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith("after-restart", restarted.Content, StringComparison.Ordinal);
            Assert.True(tailer.Checkpoint.ByteOffset < oldOffset);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task WaitsForMissingFileAndRecoversWhenItAppears()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await Task.Delay(100, cancellation.Token);
            Assert.False(runTask.IsCompleted);

            await File.WriteAllTextAsync(
                powerLog,
                Line("PowerTaskList.DebugPrintPower() - appeared"),
                LogEncoding);
            var result = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith("appeared", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task CancellationCompletesTailerAndChannel()
    {
        using var directory = new TemporaryLogDirectory();
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(tailer.Lines.Completion.IsCompletedSuccessfully);
        cancellation.Dispose();
    }

    [Fact]
    public async Task BoundedChannelAppliesBackpressureWithoutDroppingLargeBurst()
    {
        const int lineCount = 1000;
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var locator = new HearthstoneLogLocator(logRoots: [directory.Path]);
        var tailer = new PowerLogTailer(locator, channelCapacity: 8, RecoveryInterval);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runTask = tailer.RunAsync(cancellation.Token);

        try
        {
            var burst = new StringBuilder();
            for (var index = 0; index < lineCount; index++)
            {
                burst.Append(Line($"PowerTaskList.DebugPrintPower() - burst-{index:D4}"));
            }

            await File.WriteAllTextAsync(powerLog, burst.ToString(), LogEncoding);

            var received = new List<RawLogLine>(lineCount);
            while (received.Count < lineCount)
            {
                received.Add(await ReadNextAsync(tailer, cancellation.Token));
            }

            Assert.Equal(lineCount, received.Count);
            Assert.EndsWith("burst-0000", received[0].Content, StringComparison.Ordinal);
            Assert.EndsWith("burst-0999", received[^1].Content, StringComparison.Ordinal);
        }
        finally
        {
            cancellation.Cancel();
            await runTask;
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

    private static async Task<RawLogLine> ReadNextAsync(
        PowerLogTailer tailer,
        CancellationToken cancellationToken)
    {
        return await tailer.Lines.ReadAsync(cancellationToken);
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
