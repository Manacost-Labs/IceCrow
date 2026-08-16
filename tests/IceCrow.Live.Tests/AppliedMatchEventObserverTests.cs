using IceCrow.Battlegrounds;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Live.Tests;

public sealed class AppliedMatchEventObserverTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void ObserverSeesMatchStartFirstAppliedEventsInOrderAndMatchEndLast()
    {
        var observer = new CapturingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var content in BattlegroundsFixture())
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.Equal("started", observer.Sequence.First());
        Assert.Equal("ended", observer.Sequence.Last());
        Assert.Equal(1, observer.Sequence.Count(entry => entry == "started"));
        Assert.Equal(1, observer.Sequence.Count(entry => entry == "ended"));
        Assert.Equal(
            coordinator.Diagnostics.TrackingEventsApplied,
            observer.Applied.Count);
        Assert.Empty(observer.Rejected);
        Assert.False(coordinator.AppliedEventObserverDetached);
    }

    [Fact]
    public void BufferedPreStartEventsAreObservedAfterMatchStartInAppliedOrder()
    {
        var observer = new CapturingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        // Everything before the tech-level evidence is buffered, not applied.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=500"));
        _ = coordinator.Process(Line("Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]"));
        Assert.Empty(observer.Sequence);

        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2"));
        Assert.Empty(observer.Sequence);

        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TRIPLES value=0"));

        Assert.Equal("started", observer.Sequence.First());
        Assert.Contains(observer.Applied, gameEvent => gameEvent is GameEntityDeclared { EntityId: 500 });
        Assert.Contains(observer.Applied, gameEvent => gameEvent is PlayerEntityDeclared { PlayerId: 1 });
        Assert.Contains(observer.Applied, gameEvent => gameEvent is RawTagChanged { Tag: "PLAYER_TECH_LEVEL" });
        Assert.Equal(
            coordinator.Diagnostics.TrackingEventsApplied,
            observer.Applied.Count);
    }

    [Fact]
    public void SafetyRejectedEventsAreReportedAsRejectedAndNeverAsApplied()
    {
        var tracking = new TrackingSession(new TrackingSessionLimits(
            maximumTrackedEntities: 4,
            trackedEntityWarningThreshold: 1,
            maximumTagsPerEntity: 2,
            tagsPerEntityWarningThreshold: 1,
            maximumLobbyPlayers: 2,
            lobbyPlayerWarningThreshold: 1,
            maximumTimelineEventsPerPlayer: 4,
            timelineEventWarningThreshold: 1,
            maximumOpponentSnapshotsPerPlayer: 4,
            opponentSnapshotWarningThreshold: 1));
        var observer = new CapturingObserver();
        var coordinator = new LiveTrackingCoordinator(
            tracking: tracking,
            appliedEventObserver: observer);

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STEP value=MAIN_READY"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=1000 value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=1001 value=1"));

        var rejected = Assert.Single(observer.Rejected);
        Assert.Equal("1001", Assert.IsType<RawTagChanged>(rejected).Tag);
        Assert.DoesNotContain(
            observer.Applied,
            gameEvent => gameEvent is RawTagChanged { Tag: "1001" });
        Assert.Equal(1, coordinator.Diagnostics.SafetyLimitRejections);
    }

    [Fact]
    public void ConsecutiveMatchesProduceIndependentStartAndEndNotifications()
    {
        var observer = new CapturingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var content in BattlegroundsFixture(includeGameEnd: false))
        {
            _ = coordinator.Process(Line(content));
        }

        // A new game boundary ends match one; fresh confirmed evidence starts
        // match two.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("Player EntityID=9 PlayerID=9 GameAccountId=[hi=0 lo=9]"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=9 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=9 tag=PLAYER_TECH_LEVEL value=2"));

        var lifecycle = observer.Sequence
            .Where(entry => entry is "started" or "ended")
            .ToArray();
        Assert.Equal(["started", "ended", "started"], lifecycle);
    }

    [Fact]
    public void ThrowingObserverIsDetachedWhileTrackingContinuesUnaffected()
    {
        var observer = new ThrowingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var content in BattlegroundsFixture())
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.True(coordinator.AppliedEventObserverDetached);
        Assert.Equal(1, observer.Notifications);
        Assert.True(coordinator.Diagnostics.Warnings.HasFlag(
            LiveTrackingWarnings.CaptureObserverDetached));
        var snapshot = coordinator.CurrentSnapshot;
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, snapshot.Battlegrounds.Phase);
    }

    [Fact]
    public void OrdinaryNonBattlegroundsGameProducesNoNotifications()
    {
        var observer = new CapturingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=TURN value=1"));

        Assert.Empty(observer.Sequence);
        Assert.Empty(observer.Applied);
        Assert.Empty(observer.Rejected);
    }

    private int _lineIndex;

    // Real Power.log lines carry advancing timestamps; same-timestamp bursts
    // are covered by the lifecycle confirmation tests.
    private RawLogLine Line(string payload) => new(
        Timestamp.AddMilliseconds(_lineIndex++),
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);

    private static List<string> BattlegroundsFixture(bool includeGameEnd = true)
    {
        var lines = new List<string>
        {
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2",
            "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=2",
            "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=3",
            "FULL_ENTITY - Creating ID=201 CardID=BG_MINION_001",
            "tag=CARDTYPE value=MINION",
            "tag=ZONE value=PLAY",
            "tag=CONTROLLER value=2",
            "tag=ZONE_POSITION value=1",
            "tag=ATK value=7",
            "tag=HEALTH value=8",
            "TAG_CHANGE Entity=500 tag=TURN value=3",
            "TAG_CHANGE Entity=500 tag=2022 value=1",
            "TAG_CHANGE Entity=500 tag=2022 value=0",
        };
        if (includeGameEnd)
        {
            lines.Add("TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON");
        }

        return lines;
    }

    private sealed class CapturingObserver : IAppliedMatchEventObserver
    {
        public List<string> Sequence { get; } = [];

        public List<GameEvent> Applied { get; } = [];

        public List<GameEvent> Rejected { get; } = [];

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) =>
            Sequence.Add("started");

        public void OnEventApplied(GameEvent gameEvent)
        {
            Sequence.Add("applied");
            Applied.Add(gameEvent);
        }

        public void OnEventRejected(GameEvent gameEvent)
        {
            Sequence.Add("rejected");
            Rejected.Add(gameEvent);
        }

        public void OnMatchEnded(DateTimeOffset timestamp) =>
            Sequence.Add("ended");
    }

    private sealed class ThrowingObserver : IAppliedMatchEventObserver
    {
        public int Notifications { get; private set; }

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) => Throw();

        public void OnEventApplied(GameEvent gameEvent) => Throw();

        public void OnEventRejected(GameEvent gameEvent) => Throw();

        public void OnMatchEnded(DateTimeOffset timestamp) => Throw();

        private void Throw()
        {
            Notifications++;
            throw new InvalidOperationException("Observer failure must not break tracking.");
        }
    }
}
