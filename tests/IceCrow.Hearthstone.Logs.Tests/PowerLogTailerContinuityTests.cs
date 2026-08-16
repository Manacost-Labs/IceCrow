using System.Globalization;
using System.Text;

namespace IceCrow.Hearthstone.Logs.Tests;

/// <summary>
/// End-to-end regression tests for the real-client false reread (F1): an
/// append-only Power.log must never be re-read from byte zero, every accepted
/// line is delivered exactly once, and a boundary line such as CREATE_GAME is
/// never duplicated.
/// </summary>
[Collection(PowerLogTailerTestsGroup.Name)]
public sealed class PowerLogTailerContinuityTests
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(15);

    [Fact]
    public async Task CreationTimeChangeWithAppendOnlyGrowthDoesNotReread()
    {
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            await File.WriteAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - CREATE_GAME"),
                LogEncoding);
            var first = await tailer.Lines.ReadAsync(cancellation.Token);

            File.SetCreationTimeUtc(powerLog, DateTime.UtcNow.AddMinutes(-30));
            await File.AppendAllTextAsync(
                powerLog,
                Line("GameState.DebugPrintGame() - second"),
                LogEncoding,
                cancellation.Token);
            var second = await tailer.Lines.ReadAsync(cancellation.Token);

            Assert.Contains("CREATE_GAME", first.Content, StringComparison.Ordinal);
            Assert.EndsWith("second", second.Content, StringComparison.Ordinal);
            Assert.Equal(0, tailer.Diagnostics.FullRereadCount);
            await Task.Delay(100, cancellation.Token);
            Assert.False(tailer.Lines.TryRead(out _));
        }
        finally
        {
            await StopTailerAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task BurstAppendsWithIdleGapsDeliverEveryLineOnceWithoutRereads()
    {
        // Mirrors the real write pattern: bursts of appended lines separated by
        // idle gaps in which the tailer fully catches up (the state that armed
        // the old same-length rewrite check).
        const int batchCount = 30;
        const int linesPerBatch = 5;
        using var directory = new TemporaryLogDirectory();
        var powerLog = directory.GetPath("Power.log");
        await File.WriteAllTextAsync(
            powerLog,
            Line("GameState.DebugPrintGame() - CREATE_GAME"),
            LogEncoding);
        var (tailer, cancellation, runTask) = StartTailer(directory.Path);

        try
        {
            var received = new List<string>();
            received.Add((await tailer.Lines.ReadAsync(cancellation.Token)).Content);

            for (var batch = 0; batch < batchCount; batch++)
            {
                var lines = string.Concat(Enumerable
                    .Range(batch * linesPerBatch, linesPerBatch)
                    .Select(index => Line($"PowerTaskList.DebugPrintPower() - burst-{index:D4}")));
                await File.AppendAllTextAsync(powerLog, lines, LogEncoding, cancellation.Token);
                for (var index = 0; index < linesPerBatch; index++)
                {
                    received.Add((await tailer.Lines.ReadAsync(cancellation.Token)).Content);
                }

                // Idle gap: let the tailer catch up and go back to waiting.
                await Task.Delay(25, cancellation.Token);
            }

            Assert.Equal(1 + batchCount * linesPerBatch, received.Count);
            Assert.Equal(1, received.Count(content =>
                content.Contains("CREATE_GAME", StringComparison.Ordinal)));
            var burstIndexes = received
                .Where(content => content.Contains("burst-", StringComparison.Ordinal))
                .Select(content => int.Parse(
                    content.AsSpan(content.LastIndexOf('-') + 1),
                    CultureInfo.InvariantCulture))
                .ToList();
            Assert.Equal(Enumerable.Range(0, batchCount * linesPerBatch), burstIndexes);
            Assert.Equal(0, tailer.Diagnostics.FullRereadCount);
            Assert.True(tailer.Checkpoint.ByteOffset == new FileInfo(powerLog).Length);
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
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
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
