using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;
using Xunit.Abstractions;

namespace IceCrow.FixtureTool.Tests;

public sealed class LiveRecordingSoakTests(ITestOutputHelper output) : IDisposable
{
    private const int SimulatedMatches = 60;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        13,
        0,
        0,
        TimeSpan.Zero);

    private readonly string _captureRoot = Path.Combine(
        Path.GetTempPath(),
        $"icecrow-soak-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_captureRoot))
        {
            Directory.Delete(_captureRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Soak")]
    public async Task ManyCapturedConsecutiveMatchesStayBoundedAndIndependent()
    {
        var beforeMemory = GC.GetTotalMemory(forceFullCollection: true);
        var store = new PrivateCaptureStore(_captureRoot);
        var observer = new SessionObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);
        var savedCaptures = 0;
        var prunedCaptures = 0;

        for (var match = 0; match < SimulatedMatches; match++)
        {
            var offset = match * 100;
            FeedMatch(coordinator, offset);
            var completion = observer.Completions[^1];
            var recording = Assert.IsType<RecordedMatch>(completion.Match);
            Assert.Equal(
                Timestamp.AddMilliseconds(offset),
                recording.StartedAt);

            var result = await store.SaveAsync(recording);
            savedCaptures++;
            prunedCaptures += result.PrunedCapturePaths.Count;
        }

        var afterMemory = GC.GetTotalMemory(forceFullCollection: true);
        output.WriteLine($"Matches: {SimulatedMatches}");
        output.WriteLine($"Saved: {savedCaptures} · Pruned: {prunedCaptures}");
        output.WriteLine($"Stored: {store.ListCaptures().Count}");
        output.WriteLine($"ManagedMemoryBefore: {beforeMemory}");
        output.WriteLine($"ManagedMemoryAfter: {afterMemory}");

        Assert.Equal(SimulatedMatches, observer.Completions.Count);
        Assert.All(observer.Completions, static completion => Assert.NotNull(completion.Match));
        Assert.Equal(SimulatedMatches, savedCaptures);
        Assert.Equal(
            PrivateCaptureStore.DefaultMaximumCaptureCount,
            store.ListCaptures().Count);
        Assert.Equal(
            SimulatedMatches - PrivateCaptureStore.DefaultMaximumCaptureCount,
            prunedCaptures);
        Assert.False(coordinator.AppliedEventObserverDetached);
        Assert.Equal(0, coordinator.Diagnostics.SafetyLimitRejections);
    }

    private static void FeedMatch(LiveTrackingCoordinator coordinator, int offsetMilliseconds)
    {
        string[] lines =
        [
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2",
            "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=2",
            "TAG_CHANGE Entity=500 tag=TURN value=3",
            "TAG_CHANGE Entity=500 tag=2022 value=1",
            "TAG_CHANGE Entity=500 tag=2022 value=0",
            "TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON",
        ];
        for (var index = 0; index < lines.Length; index++)
        {
            var timestamp = Timestamp.AddMilliseconds(offsetMilliseconds + index);
            _ = coordinator.Process(new RawLogLine(
                timestamp,
                "Power",
                $"PowerTaskList.DebugPrintPower() - {lines[index]}",
                lines[index]));
        }
    }

    private sealed class SessionObserver : IAppliedMatchEventObserver
    {
        private readonly MatchCaptureSession _session = new();

        public List<MatchCaptureCompletion> Completions { get; } = [];

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) =>
            _session.OnMatchStarted(timestamp, localPlayerId);

        public void OnEventApplied(GameEvent gameEvent) =>
            _session.OnEventApplied(gameEvent);

        public void OnEventRejected(GameEvent gameEvent) =>
            _session.OnEventRejected(gameEvent);

        public void OnMatchEnded(DateTimeOffset timestamp)
        {
            if (_session.OnMatchEnded(timestamp) is { } completion)
            {
                Completions.Add(completion);
            }
        }
    }
}
