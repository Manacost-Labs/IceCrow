using System.Text;
using System.Globalization;

namespace IceCrow.Hearthstone.Logs.Tests;

[Collection(PowerLogTailerTestsGroup.Name)]
public sealed class PowerLogTailerRaceTests
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(15);

    [Fact]
    public async Task RapidConcurrentAppendsAreDeliveredOnceInFileOrder()
    {
        const int lineCount = 250;
        using var directory = new TemporaryLogDirectory();
        var path = directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, string.Empty, LogEncoding);
        var locator = new HearthstoneLogLocator(logRoots: [directory.Path]);
        var tailer = new PowerLogTailer(locator, channelCapacity: 32, RecoveryInterval);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runTask = tailer.RunAsync(cancellation.Token);

        try
        {
            var writer = Task.Run(async () =>
            {
                for (var batch = 0; batch < lineCount / 5; batch++)
                {
                    var lines = string.Concat(Enumerable.Range(batch * 5, 5).Select(Line));
                    await File.AppendAllTextAsync(path, lines, LogEncoding, cancellation.Token);
                    await Task.Yield();
                }
            }, cancellation.Token);

            var received = new List<int>(lineCount);
            while (received.Count < lineCount)
            {
                var line = await tailer.Lines.ReadAsync(cancellation.Token);
                received.Add(int.Parse(
                    line.Content.AsSpan(line.Content.LastIndexOf('-') + 1),
                    CultureInfo.InvariantCulture));
            }

            await writer;
            Assert.Equal(Enumerable.Range(0, lineCount), received);
            Assert.Equal(lineCount, received.Distinct().Count());
        }
        finally
        {
            cancellation.Cancel();
            await runTask;
        }
    }

    [Fact]
    public async Task DeleteRecreateAndTruncateResumeWithoutDuplicatingOldLines()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.GetPath("Power.log");
        var locator = new HearthstoneLogLocator(logRoots: [directory.Path]);
        var tailer = new PowerLogTailer(locator, recoveryInterval: RecoveryInterval);
        using var cancellation = new CancellationTokenSource();
        var runTask = tailer.RunAsync(cancellation.Token);

        try
        {
            await File.WriteAllTextAsync(path, Line(1) + Line(2), LogEncoding);
            Assert.EndsWith("race-0001", (await ReadWithTimeoutAsync(tailer, cancellation.Token)).Content, StringComparison.Ordinal);
            Assert.EndsWith("race-0002", (await ReadWithTimeoutAsync(tailer, cancellation.Token)).Content, StringComparison.Ordinal);

            File.Delete(path);
            await Task.Delay(30, cancellation.Token);
            await File.WriteAllTextAsync(path, Line(3), LogEncoding);
            Assert.EndsWith("race-0003", (await ReadWithTimeoutAsync(tailer, cancellation.Token)).Content, StringComparison.Ordinal);

            await File.WriteAllTextAsync(path, Line(4), LogEncoding, cancellation.Token);
            Assert.EndsWith("race-0004", (await ReadWithTimeoutAsync(tailer, cancellation.Token)).Content, StringComparison.Ordinal);
            await Task.Delay(75);
            Assert.False(tailer.Lines.TryRead(out _));
        }
        finally
        {
            cancellation.Cancel();
            await runTask;
        }
    }

    [Fact]
    public async Task CancellationBreaksAProducerBlockedByBackpressure()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.GetPath("Power.log");
        await File.WriteAllTextAsync(
            path,
            string.Concat(Enumerable.Range(0, 100).Select(Line)),
            LogEncoding);
        var locator = new HearthstoneLogLocator(logRoots: [directory.Path]);
        var tailer = new PowerLogTailer(locator, channelCapacity: 1, RecoveryInterval);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = tailer.RunAsync(cancellation.Token);

        _ = await tailer.Lines.ReadAsync(cancellation.Token);
        await Task.Delay(50, cancellation.Token);
        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(runTask.IsCompletedSuccessfully);
        while (tailer.Lines.TryRead(out _))
        {
        }

        await tailer.Lines.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(tailer.Lines.Completion.IsCompletedSuccessfully);
    }

    private static string Line(int index) =>
        $"D 17:00:00.0000000 PowerTaskList.DebugPrintPower() - race-{index:D4}\r\n";

    private static async Task<RawLogLine> ReadWithTimeoutAsync(
        PowerLogTailer tailer,
        CancellationToken cancellationToken) =>
        await tailer.Lines.ReadAsync(cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

}
