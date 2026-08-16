using System.Diagnostics;
using IceCrow.Hearthstone.Logs;
using IceCrow.Tracking;
using Xunit.Abstractions;

namespace IceCrow.Live.Tests;

public sealed class TrackingSoakTests(ITestOutputHelper output)
{
    private const int SimulatedTurns = 5_000;
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Soak")]
    public void LongBattlegroundsStreamKeepsLogicalStateBounded()
    {
        var beforeMemory = GC.GetTotalMemory(forceFullCollection: true);
        var coordinator = new LiveTrackingCoordinator();
        var stopwatch = Stopwatch.StartNew();

        Process(coordinator, "CREATE_GAME");
        Process(coordinator, "GameEntity EntityID=500");
        for (var playerId = 1; playerId <= 8; playerId++)
        {
            Process(
                coordinator,
                $"Player EntityID={playerId} PlayerID={playerId} GameAccountId=[hi=0 lo={playerId}]");
        }

        for (var playerId = 1; playerId <= 8; playerId++)
        {
            Process(coordinator, $"TAG_CHANGE Entity={playerId} tag=PLAYER_TECH_LEVEL value=1");
        }

        for (var opponentId = 2; opponentId <= 8; opponentId++)
        {
            for (var position = 1; position <= 7; position++)
            {
                var entityId = (opponentId * 100) + position;
                Process(coordinator, $"FULL_ENTITY - Creating ID={entityId} CardID=BG_SOAK_{opponentId}_{position}");
                Process(coordinator, "tag=CARDTYPE value=MINION");
                Process(coordinator, "tag=ZONE value=PLAY");
                Process(coordinator, $"tag=CONTROLLER value={opponentId}");
                Process(coordinator, $"tag=ZONE_POSITION value={position}");
                Process(coordinator, $"tag=ATK value={position}");
                Process(coordinator, $"tag=HEALTH value={position + 1}");
            }
        }

        for (var turn = 1; turn <= SimulatedTurns; turn++)
        {
            var opponentId = 2 + ((turn - 1) % 7);
            Process(coordinator, $"TAG_CHANGE Entity=500 tag=TURN value={(turn * 2) - 1}");
            Process(coordinator, $"TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value={opponentId}");
            Process(coordinator, "TAG_CHANGE Entity=500 tag=2022 value=1");
            Process(coordinator, "TAG_CHANGE Entity=500 tag=2022 value=0");
        }

        stopwatch.Stop();
        var afterMemory = GC.GetTotalMemory(forceFullCollection: true);
        var diagnostics = coordinator.Diagnostics;

        output.WriteLine($"Turns: {SimulatedTurns}");
        output.WriteLine($"Events: {diagnostics.TrackingEventsApplied}");
        output.WriteLine($"ElapsedMs: {stopwatch.Elapsed.TotalMilliseconds:F2}");
        output.WriteLine($"ManagedMemoryBefore: {beforeMemory}");
        output.WriteLine($"ManagedMemoryAfter: {afterMemory}");
        output.WriteLine($"EntityCount: {diagnostics.EntityCount}");
        output.WriteLine($"TagCount: {diagnostics.TagCount}");
        output.WriteLine($"TimelineEvents: {diagnostics.TimelineEventCount}");
        output.WriteLine($"OpponentSnapshots: {diagnostics.OpponentSnapshotCount}");

        Assert.InRange(diagnostics.EntityCount, 1, 64);
        Assert.InRange(diagnostics.MaximumTagsOnEntity, 1, 16);
        Assert.InRange(
            diagnostics.MaximumTimelineEventsOnPlayer,
            1,
            TrackingSessionLimits.DefaultMaximumTimelineEventsPerPlayer);
        Assert.InRange(
            diagnostics.MaximumOpponentSnapshotsOnPlayer,
            1,
            TrackingSessionLimits.DefaultMaximumOpponentSnapshotsPerPlayer);
        Assert.Equal(0, diagnostics.SafetyLimitRejections);
    }

    [Fact]
    [Trait("Category", "Soak")]
    public async Task CoordinatorCanStartCancelAndRestartRepeatedly()
    {
        const int cycles = 200;
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var coordinator = new LiveTrackingCoordinator();
            var channel = System.Threading.Channels.Channel.CreateBounded<RawLogLine>(4);
            using var cancellation = new CancellationTokenSource();
            var runTask = coordinator.RunAsync(channel.Reader, cancellationToken: cancellation.Token);
            await channel.Writer.WriteAsync(Line("CREATE_GAME"));
            cancellation.Cancel();
            channel.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(runTask.IsCompletedSuccessfully);
        }
    }

    private int _lineIndex;

    private void Process(LiveTrackingCoordinator coordinator, string payload) =>
        _ = coordinator.Process(Line(payload));

    // Advancing timestamps mirror real Power.log time so armed Battlegrounds
    // evidence is confirmed as it would be in a live match.
    private RawLogLine Line(string payload) => new(
        Timestamp.AddMilliseconds(_lineIndex++),
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);
}
