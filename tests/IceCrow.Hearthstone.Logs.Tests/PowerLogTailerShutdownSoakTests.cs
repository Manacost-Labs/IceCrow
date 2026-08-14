using System.Text;

namespace IceCrow.Hearthstone.Logs.Tests;

public sealed class PowerLogTailerShutdownSoakTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task TailerCanStartCancelDisposeWatchersAndRestartRepeatedly()
    {
        const int cycles = 75;
        using var root = new TemporaryLogDirectory();

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var iteration = root.GetPath($"cycle-{cycle:D3}");
            Directory.CreateDirectory(iteration);
            var locator = new HearthstoneLogLocator(logRoots: [iteration]);
            var tailer = new PowerLogTailer(
                locator,
                channelCapacity: 2,
                recoveryInterval: TimeSpan.FromMilliseconds(10));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var runTask = tailer.RunAsync(cancellation.Token);
            var path = Path.Combine(iteration, "Power.log");
            await File.WriteAllTextAsync(
                path,
                $"D 17:00:00.0000000 PowerTaskList.DebugPrintPower() - cycle-{cycle:D3}\r\n",
                new UTF8Encoding(false),
                cancellation.Token);

            _ = await tailer.Lines.ReadAsync(cancellation.Token);
            cancellation.Cancel();
            await runTask.WaitAsync(TimeSpan.FromSeconds(2));
            while (tailer.Lines.TryRead(out _))
            {
            }

            await tailer.Lines.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Directory.Delete(iteration, recursive: true);
            Assert.False(Directory.Exists(iteration));
        }
    }
}
