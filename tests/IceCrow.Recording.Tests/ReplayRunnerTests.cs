using IceCrow.Battlegrounds;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording.Tests.Fixtures;

namespace IceCrow.Recording.Tests;

public sealed class ReplayRunnerTests
{
    [Fact]
    public void StepsAndRunsToDeterministicCheckpoints()
    {
        var match = DeterministicMatchFixture.Create();
        var runner = new ReplayRunner(match);

        var started = runner.Step();
        Assert.Equal(0, started.CurrentEventIndex);
        Assert.Equal(BattlegroundsPhase.HeroSelection, started.Battlegrounds.Phase);

        var turnEight = runner.RunToCheckpoint("turn-8");
        Assert.Equal(8, turnEight.Battlegrounds.Turn);
        Assert.Equal(BattlegroundsPhase.Recruit, turnEight.Battlegrounds.Phase);
        Assert.Equal(4, turnEight.Battlegrounds.Lobby.GetPlayer(1)?.TavernTier);

        var combat = runner.RunToCheckpoint("combat");
        Assert.Equal(BattlegroundsPhase.Combat, combat.Battlegrounds.Phase);
        Assert.Equal(7, combat.OpponentMemory.GetLatest(2)?.Minions.Count);
    }

    [Fact]
    public void RunAllReconstructsFinalStateWithoutLosingOpponentMemory()
    {
        var state = new ReplayRunner(DeterministicMatchFixture.Create()).RunAll();

        Assert.False(state.Battlegrounds.IsActive);
        Assert.Equal(BattlegroundsPhase.GameOver, state.Battlegrounds.Phase);
        Assert.Equal(8, state.Battlegrounds.Turn);
        Assert.Equal("Reno", state.Battlegrounds.Lobby.GetPlayer(1)?.HeroName);
        Assert.Equal("Millhouse", state.Battlegrounds.Lobby.GetPlayer(2)?.HeroName);
        Assert.Equal(7, state.OpponentMemory.GetLatest(2)?.Minions.Count);
    }

    [Fact]
    public void RunUntilEarlierEventResetsAndReplaysDeterministically()
    {
        var match = DeterministicMatchFixture.Create();
        var runner = new ReplayRunner(match);
        _ = runner.RunAll();
        var turnCheckpoint = Assert.Single(
            match.Checkpoints,
            checkpoint => checkpoint.Name == "turn-8");

        var rewound = runner.RunUntilEvent(turnCheckpoint.EventIndex);

        Assert.Equal(turnCheckpoint.EventIndex, rewound.CurrentEventIndex);
        Assert.Equal(BattlegroundsPhase.Recruit, rewound.Battlegrounds.Phase);
        Assert.Null(rewound.OpponentMemory.GetLatest(2));
    }

    [Fact]
    public void ReplayingTheSameFixtureProducesTheSameProjection()
    {
        var match = DeterministicMatchFixture.Create();

        var first = Project(new ReplayRunner(match).RunAll());
        var second = Project(new ReplayRunner(match).RunAll());

        Assert.Equal(first, second);
    }

    [Fact]
    public void StepPastEndIsRejected()
    {
        var runner = new ReplayRunner(DeterministicMatchFixture.Create());
        _ = runner.RunAll();

        Assert.False(runner.CanStep);
        Assert.Throws<InvalidOperationException>(() => runner.Step());
    }

    [Fact]
    public void ReplayCanBeCancelledWithoutRealTimeDelays()
    {
        var runner = new ReplayRunner(DeterministicMatchFixture.Create());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => runner.RunAll(cancellation.Token));
        Assert.Equal(-1, runner.CurrentEventIndex);
    }

    [Fact]
    public void ReplayRejectsMoreThanTheBattlegroundsLobbyLimit()
    {
        var recorder = new MatchRecorder(new DateTimeOffset(2026, 8, 13, 21, 0, 0, TimeSpan.Zero));
        recorder.RecordMatchStarted(recorder.StartedAt);
        for (var playerId = 1; playerId <= ReplayRunner.MaximumLobbyPlayers + 1; playerId++)
        {
            recorder.Record(new RawTagChanged(
                recorder.StartedAt,
                null,
                EntityId: playerId,
                EntityName: null,
                Tag: "PLAYER_ID",
                Value: playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsCreationTag: false));
        }

        var runner = new ReplayRunner(recorder.CreateMatch());

        Assert.Throws<InvalidDataException>(() => runner.RunAll());
    }

    [Fact]
    public void ReplayRejectsAnImpossibleEightMinionOpponentBoard()
    {
        var match = DeterministicMatchFixture.Create();
        var events = match.Events.ToList();
        var combatIndex = match.Checkpoints.Single(checkpoint => checkpoint.Name == "combat").EventIndex;
        events.InsertRange(
            combatIndex - 1,
            CreateMinionEvents(entityId: 999, playerId: 2, position: 8));
        var crafted = new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            match.StartedAt,
            events);

        Assert.Throws<InvalidDataException>(() => new ReplayRunner(crafted).RunAll());
    }

    [Fact]
    public void FailedSnapshotLimitCannotBeBypassedByRetryingTheSameRunner()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);
        var recorder = new MatchRecorder(timestamp);
        recorder.RecordMatchStarted(timestamp);
        for (var combat = 0; combat <= ReplayRunner.MaximumOpponentSnapshots; combat++)
        {
            recorder.Record(new RawTagChanged(
                timestamp,
                null,
                500,
                null,
                "TURN",
                (combat + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                false));
            recorder.Record(new RawTagChanged(
                timestamp,
                null,
                500,
                null,
                "2022",
                "1",
                false));
            recorder.Record(new RawTagChanged(
                timestamp,
                null,
                500,
                null,
                "2022",
                "0",
                false));
        }

        var runner = new ReplayRunner(recorder.CreateMatch());
        Assert.Throws<InvalidDataException>(() => runner.RunAll());
        var failedAt = runner.CurrentEventIndex;

        Assert.False(runner.CanStep);
        Assert.Throws<InvalidOperationException>(() => runner.Step());
        Assert.Throws<InvalidOperationException>(() => runner.RunAll());
        Assert.Equal(failedAt, runner.CurrentEventIndex);

        runner.Reset();
        Assert.Throws<InvalidDataException>(() => runner.RunAll());
    }

    private static IEnumerable<RecordedEvent> CreateMinionEvents(
        int entityId,
        int playerId,
        int position)
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 21, 1, 0, TimeSpan.Zero);
        foreach (var (tag, value) in new[]
                 {
                     ("CARDTYPE", "MINION"),
                     ("ZONE", "PLAY"),
                     ("CONTROLLER", playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                     ("ZONE_POSITION", position.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                 })
        {
            yield return RecordedEvent.FromGameEvent(new RawTagChanged(
                timestamp,
                null,
                entityId,
                null,
                tag,
                value,
                false));
        }
    }

    private static ReplayProjection Project(ReplayState state) => new(
        state.CurrentEventIndex,
        state.Battlegrounds,
        string.Join(
            "|",
            state.Entities.Select(entity =>
                $"{entity.Id}:{entity.CardId}:{entity.Name}:" +
                string.Join(",", entity.Tags.OrderBy(static tag => tag.Key)))),
        string.Join(
            "|",
            state.OpponentMemory.GetLatest(2)?.Minions.Select(minion =>
                $"{minion.EntityId}:{minion.CardId}:{minion.Attack}:{minion.Health}:{minion.ZonePosition}") ?? []));

    private sealed record ReplayProjection(
        int EventIndex,
        BattlegroundsState Battlegrounds,
        string Entities,
        string Minions);
}
