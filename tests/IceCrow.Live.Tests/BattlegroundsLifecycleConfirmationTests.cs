using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Live.Tests;

/// <summary>
/// Regression tests for the real-client catch-up false match (F2). When the
/// running client applies a live <c>log.config</c> change it dumps its current
/// UI context as a power snapshot: CREATE_GAME, GameEntity, two players, and a
/// Battlegrounds-like tag burst all sharing one log timestamp with no
/// progression. That shape must never become a confirmed match; a real match,
/// whose log time advances immediately, must confirm exactly once.
/// </summary>
public sealed class BattlegroundsLifecycleConfirmationTests
{
    private static readonly DateTimeOffset SnapshotTimestamp = new(
        2026,
        8,
        16,
        18,
        38,
        22,
        TimeSpan.Zero);

    [Fact]
    public void CatchUpSnapshotBurstDoesNotStartAMatch()
    {
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var payload in CatchUpSnapshotLines())
        {
            var update = coordinator.Process(SnapshotLine(payload));
            Assert.False(update.StateChanged);
        }

        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);
        Assert.False(coordinator.Diagnostics.IsBattlegroundsActive);
        Assert.Equal(0, coordinator.Diagnostics.TrackingEventsApplied);
        Assert.Empty(observer.Sequence);
    }

    [Fact]
    public void CatchUpSnapshotWithCompleteStateResidueProducesNoStartAndNoEnd()
    {
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var payload in CatchUpSnapshotLines())
        {
            _ = coordinator.Process(SnapshotLine(payload));
        }

        var completeResidue = coordinator.Process(
            SnapshotLine("TAG_CHANGE Entity=1 tag=STATE value=COMPLETE"));

        Assert.False(completeResidue.StateChanged);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);
        Assert.Empty(observer.Sequence);
    }

    [Fact]
    public void RealMatchShapeConfirmsOnceAndReplaysBufferedEventsOnce()
    {
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);
        var lines = new[]
        {
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1",
            "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=2",
            "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=1",
        };

        for (var index = 0; index < lines.Length; index++)
        {
            _ = coordinator.Process(new RawLogLine(
                SnapshotTimestamp.AddMilliseconds(index),
                "Power",
                $"PowerTaskList.DebugPrintPower() - {lines[index]}",
                lines[index]));
        }

        Assert.Equal(TrackingSessionState.Active, coordinator.CurrentSnapshot.SessionState);
        Assert.Equal(1, observer.Sequence.Count(entry => entry == "started"));
        Assert.Equal(1, coordinator.CurrentSnapshot.Battlegrounds.LocalPlayerId);
        Assert.Single(observer.Applied.OfType<GameEntityDeclared>());
        Assert.Equal(2, observer.Applied.OfType<PlayerEntityDeclared>().Count());
        Assert.Equal(
            observer.Applied.Count,
            observer.Applied.Distinct().Count());
    }

    [Fact]
    public void SnapshotBurstFollowedByRealProgressionConfirmsWithSnapshotIdentities()
    {
        // The real session's shape: the catch-up dump armed the evidence and
        // the same game context continued into the live match minutes later
        // without a second CREATE_GAME. The armed candidate must confirm on the
        // first later-timestamped event and keep the snapshot's declarations.
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var payload in CatchUpSnapshotLines())
        {
            _ = coordinator.Process(SnapshotLine(payload));
        }

        Assert.Empty(observer.Sequence);

        var liveLine = "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=2";
        var confirming = coordinator.Process(new RawLogLine(
            SnapshotTimestamp.AddSeconds(40),
            "Power",
            $"PowerTaskList.DebugPrintPower() - {liveLine}",
            liveLine));

        Assert.True(confirming.StateChanged);
        Assert.Equal(TrackingSessionState.Active, coordinator.CurrentSnapshot.SessionState);
        Assert.Equal(1, observer.Sequence.Count(entry => entry == "started"));
        Assert.Equal(2, observer.Applied.OfType<PlayerEntityDeclared>().Count());
    }

    [Fact]
    public void ConfirmedMatchStartUsesEvidenceTimeNotTheStaleCatchUpBoundary()
    {
        // F4: all three real captures were stamped with the 18:38:22 catch-up
        // CREATE_GAME time even though the real match began ~18:39. The
        // confirmed start must carry the confirming evidence timestamp.
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        foreach (var payload in CatchUpSnapshotLines())
        {
            _ = coordinator.Process(SnapshotLine(payload));
        }

        var confirmingAt = SnapshotTimestamp.AddSeconds(40);
        var liveLine = "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=2";
        _ = coordinator.Process(new RawLogLine(
            confirmingAt,
            "Power",
            $"PowerTaskList.DebugPrintPower() - {liveLine}",
            liveLine));

        Assert.Equal(confirmingAt, Assert.Single(observer.Starts));
    }

    [Fact]
    public void ConsecutiveConfirmedMatchesKeepMonotonicStartTimes()
    {
        var observer = new RecordingObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);
        var lines = new[]
        {
            "CREATE_GAME",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1",
            "TAG_CHANGE Entity=1 tag=PLAYER_TRIPLES value=0",
            "CREATE_GAME",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1",
            "TAG_CHANGE Entity=1 tag=PLAYER_TRIPLES value=0",
        };

        for (var index = 0; index < lines.Length; index++)
        {
            _ = coordinator.Process(new RawLogLine(
                SnapshotTimestamp.AddMilliseconds(index),
                "Power",
                $"PowerTaskList.DebugPrintPower() - {lines[index]}",
                lines[index]));
        }

        Assert.Equal(2, observer.Starts.Count);
        Assert.True(observer.Starts[1] > observer.Starts[0]);
        Assert.Equal(SnapshotTimestamp.AddMilliseconds(2), observer.Starts[0]);
        Assert.Equal(SnapshotTimestamp.AddMilliseconds(5), observer.Starts[1]);
    }

    [Fact]
    public void DetectorConfirmsExactlyOnceAndOnlyOnLaterTimestamp()
    {
        var detector = new BattlegroundsLifecycleDetector();
        _ = detector.Observe(new GameCreated(SnapshotTimestamp));

        var armed = detector.Observe(Tag("PLAYER_TECH_LEVEL", "1", SnapshotTimestamp));
        var sameInstant = detector.Observe(Tag("PLAYER_TRIPLES", "0", SnapshotTimestamp));
        var confirmed = detector.Observe(Tag("100", "1", SnapshotTimestamp.AddMilliseconds(1)));
        var saturated = detector.Observe(Tag("PLAYER_TECH_LEVEL", "2", SnapshotTimestamp.AddMilliseconds(2)));

        Assert.Equal(BattlegroundsLifecycleAction.None, armed.Action);
        Assert.Equal(BattlegroundsLifecycleAction.None, sameInstant.Action);
        Assert.Equal(BattlegroundsLifecycleAction.StartBattlegrounds, confirmed.Action);
        Assert.Equal(BattlegroundsLifecycleEvidence.PlayerTechLevel, confirmed.Evidence);
        Assert.Equal(BattlegroundsLifecycleAction.None, saturated.Action);
    }

    [Fact]
    public void CompleteStateClearsArmedEvidenceSoLaterEventsDoNotConfirmIt()
    {
        var detector = new BattlegroundsLifecycleDetector();
        _ = detector.Observe(new GameCreated(SnapshotTimestamp));

        _ = detector.Observe(Tag("PLAYER_TECH_LEVEL", "1", SnapshotTimestamp));
        var ended = detector.Observe(Tag("STATE", "COMPLETE", SnapshotTimestamp));
        var afterEnd = detector.Observe(Tag("100", "1", SnapshotTimestamp.AddSeconds(5)));

        Assert.Equal(BattlegroundsLifecycleAction.EndMatch, ended.Action);
        Assert.Equal(BattlegroundsLifecycleAction.None, afterEnd.Action);
    }

    private static RawTagChanged Tag(string tag, string value, DateTimeOffset timestamp) =>
        new(timestamp, null, 1, null, tag, value, IsCreationTag: false);

    private static RawLogLine SnapshotLine(string payload) => new(
        SnapshotTimestamp,
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);

    /// <summary>
    /// Sanitized shape of real capture A: one CREATE_GAME, a game entity, two
    /// player declarations, and a Battlegrounds-like tag burst, every event on
    /// the same timestamp with no turn progression. No private identifiers.
    /// </summary>
    private static string[] CatchUpSnapshotLines() =>
    [
        "CREATE_GAME",
        "GameEntity EntityID=1",
        "Player EntityID=2 PlayerID=1 GameAccountId=[hi=0 lo=1]",
        "Player EntityID=3 PlayerID=2 GameAccountId=[hi=0 lo=2]",
        "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=1",
        "TAG_CHANGE Entity=3 tag=PLAYER_TECH_LEVEL value=1",
        "TAG_CHANGE Entity=2 tag=PLAYER_TRIPLES value=0",
        "TAG_CHANGE Entity=1 tag=TURN value=0",
        "TAG_CHANGE Entity=2 tag=NEXT_OPPONENT_PLAYER_ID value=2",
    ];

    private sealed class RecordingObserver : IAppliedMatchEventObserver
    {
        public List<string> Sequence { get; } = [];

        public List<GameEvent> Applied { get; } = [];

        public List<DateTimeOffset> Starts { get; } = [];

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
        {
            Sequence.Add("started");
            Starts.Add(timestamp);
        }

        public void OnEventApplied(GameEvent gameEvent)
        {
            Sequence.Add("applied");
            Applied.Add(gameEvent);
        }

        public void OnEventRejected(GameEvent gameEvent) =>
            Sequence.Add("rejected");

        public void OnMatchEnded(DateTimeOffset timestamp) =>
            Sequence.Add("ended");
    }
}
