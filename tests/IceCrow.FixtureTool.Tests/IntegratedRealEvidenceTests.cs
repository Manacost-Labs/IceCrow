using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;
using IceCrow.Tracking;

namespace IceCrow.FixtureTool.Tests;

/// <summary>
/// One integrated regression over the merged real-client fixes, using
/// sanitized real-shaped events (not approved real evidence): a same-timestamp
/// catch-up burst arms but never starts a match, a later game-step advance
/// confirms it once at the confirmation time, bare-name TURN and combat tags
/// drive phases, one opponent board is captured, and the resulting capture
/// replays to the same semantic state.
/// </summary>
public sealed class IntegratedRealEvidenceTests
{
    private static readonly DateTimeOffset BurstTimestamp = new(
        2026,
        8,
        17,
        11,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task CatchUpThenRealProgressionProducesOneTruthfulReplayableMatch()
    {
        var observer = new SessionObserver();
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);

        // Catch-up burst: every line on one timestamp, no progression.
        string[] burst =
        [
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=4 GameAccountId=[hi=0 lo=4]",
            "Player EntityID=2 PlayerID=6 GameAccountId=[hi=0 lo=6]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1",
            "TAG_CHANGE Entity=500 tag=TURN value=0",
        ];
        foreach (var payload in burst)
        {
            _ = coordinator.Process(Line(payload, BurstTimestamp));
        }

        Assert.Empty(observer.Starts);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);

        // Real progression confirms exactly once, at the confirmation time.
        var confirmAt = BurstTimestamp.AddMilliseconds(400);
        _ = coordinator.Process(Line("TAG_CHANGE Entity=500 tag=STEP value=BEGIN_MULLIGAN", confirmAt));
        Assert.Equal(confirmAt, Assert.Single(observer.Starts));

        var live = new[]
        {
            "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=6",
            "FULL_ENTITY - Creating ID=301 CardID=BG_MINION_001",
            "tag=CARDTYPE value=MINION",
            "tag=ZONE value=PLAY",
            "tag=CONTROLLER value=6",
            "tag=ZONE_POSITION value=1",
            "tag=ATK value=7",
            "tag=HEALTH value=8",
            "TAG_CHANGE Entity=GameEntity tag=TURN value=1",
            "TAG_CHANGE Entity=GameEntity tag=2022 value=1",
            "TAG_CHANGE Entity=GameEntity tag=2022 value=0",
            "TAG_CHANGE Entity=1 tag=PLAYSTATE value=LOST",
        };
        for (var index = 0; index < live.Length; index++)
        {
            _ = coordinator.Process(Line(live[index], confirmAt.AddSeconds(index + 1)));
        }

        var snapshot = coordinator.CurrentSnapshot;
        Assert.Single(observer.Starts);
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
        Assert.Equal(1, snapshot.Battlegrounds.Turn);
        Assert.Equal(BattlegroundsPhase.GameOver, snapshot.Battlegrounds.Phase);
        var board = Assert.IsType<BoardSnapshot>(snapshot.OpponentMemory.GetLatest(6));
        Assert.Equal(7, Assert.Single(board.Minions).Attack);
        Assert.Equal(0, coordinator.Diagnostics.UnresolvedNamedReferences);

        // The capture must survive the full official persistence boundary
        // (serialize, then deserialize) before replay proves equivalence.
        var completion = Assert.Single(observer.Completions);
        var recording = Assert.IsType<RecordedMatch>(completion.Match);
        Assert.Equal(RecordedEventType.MatchStarted, recording.Events[0].Type);
        Assert.Equal(RecordedEventType.MatchEnded, recording.Events[^1].Type);
        Assert.Equal(confirmAt, recording.Events[0].Timestamp);

        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, recording);
        stream.Position = 0;
        var loaded = await RecordingSerializer.DeserializeAsync(stream);
        Assert.Equal(recording.Events, loaded.Events);

        var replay = new ReplayRunner(loaded).RunAll();
        Assert.Equal(snapshot.Battlegrounds.Turn, replay.Battlegrounds.Turn);
        Assert.Equal(snapshot.Battlegrounds.Phase, replay.Battlegrounds.Phase);
        var replayBoard = Assert.IsType<BoardSnapshot>(replay.OpponentMemory.GetLatest(6));
        Assert.Equal(
            board.Minions.Select(static minion => (minion.EntityId, minion.Attack, minion.Health)),
            replayBoard.Minions.Select(static minion => (minion.EntityId, minion.Attack, minion.Health)));
        Assert.Equal(0, replay.UnresolvedNamedReferences);
    }

    private static RawLogLine Line(string payload, DateTimeOffset timestamp) => new(
        timestamp,
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);

    private sealed class SessionObserver : IAppliedMatchEventObserver
    {
        private readonly MatchCaptureSession _session = new();

        public List<DateTimeOffset> Starts { get; } = [];

        public List<MatchCaptureCompletion> Completions { get; } = [];

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
        {
            Starts.Add(timestamp);
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
