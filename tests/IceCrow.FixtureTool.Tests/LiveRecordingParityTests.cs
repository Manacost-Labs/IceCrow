using IceCrow.Battlegrounds;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;
using IceCrow.Tracking;

namespace IceCrow.FixtureTool.Tests;

/// <summary>
/// Proves that a capture taken from the live applied-event stream replays to
/// the same authoritative state the live pipeline reached, using semantic
/// comparison rather than presentation strings or object identity.
/// </summary>
public sealed class LiveRecordingParityTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task LiveMatchRoundTripsThroughSerializedRecordingToEquivalentState()
    {
        var observer = new CaptureObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        Feed(coordinator, BattlegroundsFixture());

        var completion = Assert.Single(observer.Completions);
        var recording = Assert.IsType<RecordedMatch>(completion.Match);
        Assert.Equal(RecordedEventType.MatchStarted, recording.Events[0].Type);
        Assert.Equal(RecordedEventType.MatchEnded, recording.Events[^1].Type);
        Assert.Equal(
            coordinator.Diagnostics.TrackingEventsApplied + 2,
            recording.Events.Count);

        // The capture must survive its serialized form, not just the object graph.
        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, recording);
        stream.Position = 0;
        var loaded = await RecordingSerializer.DeserializeAsync(stream);

        var replay = new ReplayRunner(loaded).RunAll();
        AssertSemanticParity(coordinator.CurrentSnapshot, replay);
    }

    [Fact]
    public void BufferedPreStartEventsReplayWithoutLossOrDuplication()
    {
        var observer = new CaptureObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);
        var lines = BattlegroundsFixture();

        // Only the first four lines are processed before Battlegrounds evidence
        // arrives, so every one of them reaches the recording via the buffer.
        Feed(coordinator, lines);

        var recording = Assert.Single(observer.Completions).Match;
        Assert.NotNull(recording);
        var recordedEntityDeclarations = recording.Events.Count(static recordedEvent =>
            recordedEvent.Type == RecordedEventType.GameEntityDeclared);
        var recordedPlayerDeclarations = recording.Events.Count(static recordedEvent =>
            recordedEvent.Type == RecordedEventType.PlayerEntityDeclared);
        Assert.Equal(1, recordedEntityDeclarations);
        Assert.Equal(2, recordedPlayerDeclarations);

        var replay = new ReplayRunner(recording).RunAll();
        AssertSemanticParity(coordinator.CurrentSnapshot, replay);
    }

    [Fact]
    public void ConsecutiveMatchesProduceIndependentReplayableRecordings()
    {
        var observer = new CaptureObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        Feed(coordinator, BattlegroundsFixture());
        var liveAfterFirst = coordinator.CurrentSnapshot;
        Feed(coordinator, BattlegroundsFixture(), offsetMilliseconds: 1000);

        Assert.Equal(2, observer.Completions.Count);
        var first = Assert.IsType<RecordedMatch>(observer.Completions[0].Match);
        var second = Assert.IsType<RecordedMatch>(observer.Completions[1].Match);
        Assert.True(second.StartedAt > first.StartedAt);

        var firstReplay = new ReplayRunner(first).RunAll();
        AssertSemanticParity(liveAfterFirst, firstReplay);
        Assert.Single(firstReplay.OpponentMemory.Histories);

        // Match two is a fresh recording: replaying it alone reproduces the
        // second live match, including opponent memory rebuilt from scratch.
        var secondReplay = new ReplayRunner(second).RunAll();
        AssertSemanticParity(coordinator.CurrentSnapshot, secondReplay);
        Assert.Single(secondReplay.OpponentMemory.Histories);
        Assert.Equal(first.Events.Count, second.Events.Count);
    }

    [Fact]
    public void SafetyRejectedEventsNeverProduceAReplayableRecording()
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
        var observer = new CaptureObserver();
        var coordinator = new LiveTrackingCoordinator(
            tracking: tracking,
            appliedEventObserver: observer);

        Feed(
            coordinator,
            [
                "CREATE_GAME",
                "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1",
                "TAG_CHANGE Entity=1 tag=1000 value=1",
                "TAG_CHANGE Entity=1 tag=1001 value=1",
                "CREATE_GAME",
            ]);

        Assert.True(coordinator.Diagnostics.SafetyLimitRejections > 0);
        var completion = Assert.Single(observer.Completions);
        Assert.Null(completion.Match);
        Assert.Contains("safety limit", completion.DiscardReason, StringComparison.Ordinal);
    }

    private static void Feed(
        LiveTrackingCoordinator coordinator,
        List<string> lines,
        int offsetMilliseconds = 0)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var timestamp = Timestamp.AddMilliseconds(offsetMilliseconds + index);
            _ = coordinator.Process(new RawLogLine(
                timestamp,
                "Power",
                $"PowerTaskList.DebugPrintPower() - {lines[index]}",
                lines[index]));
        }
    }

    private static void AssertSemanticParity(TrackingSnapshot live, ReplayState replay)
    {
        var liveState = live.Battlegrounds;
        var replayState = replay.Battlegrounds;
        Assert.Equal(liveState.IsActive, replayState.IsActive);
        Assert.Equal(liveState.Turn, replayState.Turn);
        Assert.Equal(liveState.Phase, replayState.Phase);
        Assert.Equal(liveState.LocalPlayerId, replayState.LocalPlayerId);
        Assert.Equal(liveState.CurrentOpponentPlayerId, replayState.CurrentOpponentPlayerId);
        Assert.Equal(liveState.Lobby.Count, replayState.Lobby.Count);

        Assert.Equal(
            live.OpponentMemory.Histories.Keys.Order(),
            replay.OpponentMemory.Histories.Keys.Order());
        foreach (var playerId in live.OpponentMemory.Histories.Keys)
        {
            var liveBoard = live.OpponentMemory.GetLatest(playerId);
            var replayBoard = replay.OpponentMemory.GetLatest(playerId);
            Assert.NotNull(liveBoard);
            Assert.NotNull(replayBoard);
            Assert.Equal(liveBoard.Turn, replayBoard.Turn);
            Assert.Equal(
                liveBoard.Minions.Select(Describe),
                replayBoard.Minions.Select(Describe));
        }

        Assert.Equal(
            live.LobbyTimeline.Players.Select(static player =>
                (player.PlayerId, player.TavernTier, player.Triples, player.Events.Count)),
            replay.LobbyTimeline.Players.Select(static player =>
                (player.PlayerId, player.TavernTier, player.Triples, player.Events.Count)));
    }

    private static (int EntityId, string? CardId, int Attack, int Health, int Position) Describe(
        IceCrow.Battlegrounds.Memory.MinionSnapshot minion) =>
        (minion.EntityId, minion.CardId, minion.Attack, minion.Health, minion.ZonePosition);

    private static List<string> BattlegroundsFixture() =>
    [
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
        "TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON",
    ];

    private sealed class CaptureObserver : IAppliedMatchEventObserver
    {
        private readonly MatchCaptureSession _session = new();

        public List<MatchCaptureCompletion> Completions { get; } = [];

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
        {
            _session.OnMatchStarted(timestamp, localPlayerId);
        }

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
