using System.Text;
using IceCrow.Hearthstone.Logs;

namespace IceCrow.Hearthstone.Logs.Tests;

[Collection(PowerLogTailerTestsGroup.Name)]
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
    public async Task TimestampOnlyTouchAtEndOfFileDoesNotReplayExistingLines()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        await File.WriteAllTextAsync(
            powerLog,
            Line("GameState.DebugPrintGame() - original"),
            LogEncoding);
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            _ = await ReadNextAsync(tailer, cancellation.Token);
            var offset = tailer.Checkpoint.ByteOffset;

            File.SetLastWriteTimeUtc(powerLog, DateTime.UtcNow.AddSeconds(2));
            await Task.Delay(150, cancellation.Token);

            Assert.Equal(offset, tailer.Checkpoint.ByteOffset);
            Assert.False(tailer.Lines.TryRead(out _));
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task SameLengthRewriteAfterUnchangedPrefixResetsTheCheckpoint()
    {
        const int lineCount = 100;
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var lines = Enumerable.Range(0, lineCount)
            .Select(index => Line($"GameState.DebugPrintGame() - line-{index:D3}-{new string('A', 64)}"))
            .ToArray();
        await File.WriteAllTextAsync(powerLog, string.Concat(lines), LogEncoding);
        var (tailer, cancellation, runTask) = StartTailer(directory.Path, TimeSpan.FromSeconds(30));

        try
        {
            for (var index = 0; index < lineCount; index++)
            {
                _ = await ReadNextAsync(tailer, cancellation.Token);
            }

            await WaitForCheckpointAsync(
                tailer,
                new FileInfo(powerLog).Length,
                cancellation.Token);
            var originalOffset = tailer.Checkpoint.ByteOffset;
            var replacementOffset = LogEncoding.GetByteCount(string.Concat(lines[..^1])) +
                                    LogEncoding.GetByteCount(lines[^1]) - 4;
            await using (var stream = new FileStream(
                powerLog,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                stream.Seek(replacementOffset, SeekOrigin.Begin);
                await stream.WriteAsync("B"u8.ToArray(), cancellation.Token);
                await stream.FlushAsync(cancellation.Token);
            }

            File.SetLastWriteTimeUtc(
                powerLog,
                tailer.Checkpoint.LastWriteAt.UtcDateTime.AddSeconds(1));

            var replayed = await ReadNextAsync(tailer, cancellation.Token);

            Assert.EndsWith(new string('A', 64), replayed.Content, StringComparison.Ordinal);
            Assert.Equal(originalOffset, new FileInfo(powerLog).Length);
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
            await WaitForCheckpointAsync(
                tailer,
                new FileInfo(powerLog).Length,
                cancellation.Token);
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
        StartTailer(string logRoot, TimeSpan? timeout = null)
    {
        var locator = new HearthstoneLogLocator(logRoots: [logRoot]);
        var tailer = new PowerLogTailer(locator, recoveryInterval: RecoveryInterval);
        var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        return (tailer, cancellation, tailer.RunAsync(cancellation.Token));
    }

    private static async Task<RawLogLine> ReadNextAsync(
        PowerLogTailer tailer,
        CancellationToken cancellationToken)
    {
        return await tailer.Lines.ReadAsync(cancellationToken);
    }

    private static async Task WaitForCheckpointAsync(
        PowerLogTailer tailer,
        long expectedOffset,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var checkpoint = tailer.Checkpoint;
            if (checkpoint.ByteOffset == expectedOffset &&
                (expectedOffset == 0 || checkpoint.PrefixFingerprintLength > 0))
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }
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
