using System.Threading.Channels;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.Live.Tests;

public sealed class GameSessionCoordinatorTests
{
    private const string MetadataPrefix = "GameState.DebugPrintGame() - ";

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        9,
        6,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void RankedStandardGameProducesOneCompletedMatchAndNeverStartsBattlegrounds()
    {
        var coordinator = new GameSessionCoordinator();

        var updates = CreateRankedFixture().Select(content => coordinator.Process(Line(content))).ToList();

        var completed = Assert.Single(updates, update => update.CompletedMatch is not null);
        Assert.True(completed.StateChanged);
        var summary = Assert.IsType<ConstructedMatchSummary>(completed.CompletedMatch);
        Assert.Equal(GameMode.Ranked, summary.Mode);
        Assert.Equal(ConstructedFormat.Standard, summary.Format);
        Assert.Equal(ConstructedMatchResult.Won, summary.Result);
        Assert.Equal("HERO_01", summary.PlayerHeroCardId);
        Assert.Equal(["A", "B", "C"], summary.Mulligan.Initial);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.Battlegrounds.CurrentSnapshot.SessionState);
        Assert.Equal(0, coordinator.Diagnostics.Battlegrounds.TrackingEventsApplied);
        Assert.False(coordinator.Diagnostics.Battlegrounds.IsBattlegroundsActive);
        Assert.Equal(GameSessionRoute.Constructed, coordinator.Route);
        Assert.Equal(GameMode.Ranked, updates[^1].ActiveMode);
        Assert.Equal(1, coordinator.Diagnostics.CompletedMatches);
        Assert.Equal(CreateRankedFixture().Count, coordinator.Diagnostics.RawLinesReceived);
        // Everything after the metadata block bypasses the Battlegrounds
        // coordinator entirely; only the boundary and metadata reached it.
        Assert.All(updates.Skip(7), update => Assert.Null(update.Battlegrounds));
        Assert.Equal(7, coordinator.Diagnostics.BattlegroundsLinesRouted);
    }

    [Fact]
    public void BattlegroundsFixtureStillStartsAndEndsAMatchThroughTheCoordinator()
    {
        var coordinator = new GameSessionCoordinator();

        var updates = CreateBattlegroundsFixture(includeMetadata: true)
            .Select(content => coordinator.Process(Line(content)))
            .ToList();

        var snapshot = coordinator.Battlegrounds.CurrentSnapshot;
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, snapshot.Battlegrounds.Phase);
        Assert.Equal(2, snapshot.Battlegrounds.Turn);
        Assert.Equal(1, snapshot.Battlegrounds.LocalPlayerId);
        var board = Assert.IsType<BoardSnapshot>(snapshot.OpponentMemory.GetLatest(2));
        Assert.Equal(7, Assert.Single(board.Minions).Attack);
        Assert.DoesNotContain(updates, update => update.CompletedMatch is not null);
        Assert.Equal(GameSessionRoute.Battlegrounds, coordinator.Route);
        Assert.Equal(GameMode.Battlegrounds, coordinator.Diagnostics.ActiveMode);
        Assert.All(updates, update => Assert.NotNull(update.Battlegrounds));
        Assert.True(updates[^1].StateChanged);
    }

    [Fact]
    public void LinesAfterBattlegroundsMetadataAreNotAppliedToTheConstructedTracker()
    {
        var coordinator = new GameSessionCoordinator();
        var fixture = CreateBattlegroundsFixture(includeMetadata: true);
        var metadataEnd = fixture.FindLastIndex(content => content.StartsWith(MetadataPrefix, StringComparison.Ordinal));

        foreach (var content in fixture.Take(metadataEnd + 1))
        {
            _ = coordinator.Process(Line(content));
        }

        var routedBefore = coordinator.Diagnostics.ConstructedEventsRouted;
        Assert.Equal(0, coordinator.Diagnostics.ConstructedEntityCount);

        foreach (var content in fixture.Skip(metadataEnd + 1))
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.Equal(routedBefore, coordinator.Diagnostics.ConstructedEventsRouted);
        Assert.Equal(0, coordinator.Diagnostics.ConstructedEntityCount);
        Assert.Equal(TrackingSessionState.Ended, coordinator.Battlegrounds.CurrentSnapshot.SessionState);
    }

    [Fact]
    public void BackToBackRankedThenBattlegroundsGamesAreIsolated()
    {
        var coordinator = new GameSessionCoordinator();
        var completed = new List<ConstructedMatchSummary>();

        foreach (var content in CreateRankedFixture().Concat(CreateBattlegroundsFixture(includeMetadata: true)))
        {
            var update = coordinator.Process(Line(content));
            if (update.CompletedMatch is { } summary)
            {
                completed.Add(summary);
            }
        }

        Assert.Equal(ConstructedMatchResult.Won, Assert.Single(completed).Result);
        var snapshot = coordinator.Battlegrounds.CurrentSnapshot;
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, snapshot.Battlegrounds.Phase);
        Assert.Equal(2, snapshot.Battlegrounds.Turn);
        Assert.Equal(1, coordinator.Diagnostics.CompletedMatches);
        Assert.Equal(1, coordinator.Diagnostics.IgnoredModeGames);
        Assert.Equal(2, coordinator.Diagnostics.GamesSeen);
    }

    [Fact]
    public void MissingMetadataFeedsBothTrackersWithoutCrashing()
    {
        var coordinator = new GameSessionCoordinator();

        var updates = CreateBattlegroundsFixture(includeMetadata: false)
            .Concat(CreateRankedFixture().Where(content => !content.StartsWith(MetadataPrefix, StringComparison.Ordinal)))
            .Select(content => coordinator.Process(Line(content)))
            .ToList();

        Assert.Equal(GameSessionRoute.Both, coordinator.Route);
        Assert.Equal(GameMode.Unknown, coordinator.Diagnostics.ActiveMode);
        Assert.All(updates, update => Assert.NotNull(update.Battlegrounds));
        Assert.DoesNotContain(updates, update => update.CompletedMatch is not null);
        Assert.Equal(2, coordinator.Diagnostics.IgnoredModeGames);
        Assert.True(coordinator.Diagnostics.ConstructedEventsRouted > 0);
        Assert.Equal(TrackingSessionState.Ended, coordinator.Battlegrounds.CurrentSnapshot.SessionState);
    }

    [Fact]
    public void CasualGameFeedsNeitherTrackerAfterItsMetadata()
    {
        var coordinator = new GameSessionCoordinator();
        var fixture = CreateRankedFixture(gameType: "GT_CASUAL");
        var metadataEnd = fixture.FindLastIndex(content => content.StartsWith(MetadataPrefix, StringComparison.Ordinal));
        foreach (var content in fixture.Take(metadataEnd + 1))
        {
            _ = coordinator.Process(Line(content));
        }

        var before = coordinator.Diagnostics;
        var updates = fixture.Skip(metadataEnd + 1).Select(content => coordinator.Process(Line(content))).ToList();

        Assert.Equal(GameSessionRoute.None, coordinator.Route);
        Assert.Equal(before.BattlegroundsLinesRouted, coordinator.Diagnostics.BattlegroundsLinesRouted);
        Assert.Equal(before.ConstructedEventsRouted, coordinator.Diagnostics.ConstructedEventsRouted);
        Assert.Equal(before.RawLinesReceived + updates.Count, coordinator.Diagnostics.RawLinesReceived);
        Assert.All(updates, update => Assert.Null(update.Battlegrounds));
        Assert.DoesNotContain(updates, update => update.CompletedMatch is not null);
    }

    [Fact]
    public void NonParsedLinesAreCountedAtTheSessionLevelAndNotRoutedDuringConstructedGames()
    {
        var coordinator = new GameSessionCoordinator();
        foreach (var content in CreateRankedFixture().Take(7))
        {
            _ = coordinator.Process(Line(content));
        }

        var routed = coordinator.Diagnostics.BattlegroundsLinesRouted;
        var unknown = coordinator.Process(Line("META_DATA - Meta=TARGET Data=0 InfoCount=0"));
        var malformed = coordinator.Process(Line("GameEntity EntityID=not-an-id"));

        Assert.Equal(PowerParseStatus.Unknown, unknown.ParseResult.Status);
        Assert.Equal(PowerParseStatus.Malformed, malformed.ParseResult.Status);
        Assert.Null(unknown.Battlegrounds);
        Assert.Null(malformed.Battlegrounds);
        Assert.Equal(1, coordinator.Diagnostics.Unknown);
        Assert.Equal(1, coordinator.Diagnostics.Malformed);
        Assert.Equal(routed, coordinator.Diagnostics.BattlegroundsLinesRouted);
    }

    [Fact]
    public void ProcessParsedMatchesProcessForTheSameLines()
    {
        var direct = new LiveTrackingCoordinator();
        var parser = new PowerLineParser();
        var preParsed = new LiveTrackingCoordinator(parser: new PowerLineParser());
        var lines = CreateBattlegroundsFixture(includeMetadata: true)
            .Concat(["META_DATA - Meta=TARGET Data=0 InfoCount=0", "GameEntity EntityID=not-an-id"])
            .Concat(CreateRankedFixture())
            .Select(Line)
            .ToList();

        foreach (var line in lines)
        {
            var expected = direct.Process(line);
            var actual = preParsed.ProcessParsed(line, parser.Parse(line.Content, line.Timestamp));

            Assert.Equal(expected.ParseResult.Status, actual.ParseResult.Status);
            Assert.Equal(expected.StateChanged, actual.StateChanged);
            Assert.Equal(expected.Snapshot?.SessionState, actual.Snapshot?.SessionState);
            Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        }

        Assert.Equal(direct.CurrentSnapshot.SessionState, preParsed.CurrentSnapshot.SessionState);
        Assert.Equal(direct.CurrentSnapshot.Battlegrounds, preParsed.CurrentSnapshot.Battlegrounds);
    }

    [Fact]
    public void AppliedEventObserverIsPassedThroughToTheBattlegroundsCoordinator()
    {
        var observer = new CountingObserver();
        var coordinator = new GameSessionCoordinator(appliedEventObserver: observer);

        foreach (var content in CreateRankedFixture().Concat(CreateBattlegroundsFixture(includeMetadata: true)))
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.Equal(1, observer.Started);
        Assert.Equal(1, observer.Ended);
        Assert.Equal(coordinator.Diagnostics.Battlegrounds.TrackingEventsApplied, observer.Applied);
    }

    [Fact]
    public async Task RunAsyncStopsCleanlyWhenCancellationIsRequested()
    {
        var coordinator = new GameSessionCoordinator();
        var channel = Channel.CreateBounded<RawLogLine>(1);
        using var cancellation = new CancellationTokenSource();
        var processed = 0;
        var runTask = coordinator.RunAsync(channel.Reader, _ => processed++, cancellation.Token);
        await channel.Writer.WriteAsync(Line("CREATE_GAME"));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(runTask.IsCompletedSuccessfully);
        Assert.True(processed <= 1);
    }

    [Fact]
    public void ResetClearsBothTrackersAndTheRoute()
    {
        var coordinator = new GameSessionCoordinator();
        foreach (var content in CreateRankedFixture().Take(7))
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.Equal(GameSessionRoute.Constructed, coordinator.Route);

        coordinator.Reset();

        Assert.Equal(GameSessionRoute.Both, coordinator.Route);
        Assert.False(coordinator.Constructed.IsGameOpen);
        Assert.Equal(GameMode.Unknown, coordinator.Diagnostics.ActiveMode);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.Battlegrounds.CurrentSnapshot.SessionState);
    }

    private int _lineIndex;

    private RawLogLine Line(string payload)
    {
        var content = payload.StartsWith(MetadataPrefix, StringComparison.Ordinal)
            ? payload
            : $"PowerTaskList.DebugPrintPower() - {payload}";
        return new RawLogLine(Timestamp.AddMilliseconds(_lineIndex++), "Power", content, payload);
    }

    // CREATE_GAME, the game entity, both players, then the metadata block:
    // seven lines the Battlegrounds coordinator sees before routing narrows.
    private static List<string> CreateRankedFixture(string gameType = "GT_RANKED") =>
    [
        "CREATE_GAME",
        "GameEntity EntityID=1",
        "Player EntityID=2 PlayerID=1 GameAccountId=[hi=0 lo=1]",
        "Player EntityID=3 PlayerID=2 GameAccountId=[hi=0 lo=2]",
        $"{MetadataPrefix}BuildNumber=224857",
        $"{MetadataPrefix}GameType={gameType}",
        $"{MetadataPrefix}FormatType=FT_STANDARD",
        "FULL_ENTITY - Creating ID=64 CardID=HERO_01",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=HERO",
        "tag=ZONE value=PLAY",
        "FULL_ENTITY - Creating ID=66 CardID=HERO_08",
        "tag=CONTROLLER value=2",
        "tag=CARDTYPE value=HERO",
        "tag=ZONE value=PLAY",
        "FULL_ENTITY - Creating ID=10 CardID=A",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=MINION",
        "tag=ZONE value=HAND",
        "tag=ZONE_POSITION value=1",
        "FULL_ENTITY - Creating ID=11 CardID=B",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=MINION",
        "tag=ZONE value=HAND",
        "tag=ZONE_POSITION value=2",
        "FULL_ENTITY - Creating ID=12 CardID=C",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=MINION",
        "tag=ZONE value=HAND",
        "tag=ZONE_POSITION value=3",
        "FULL_ENTITY - Creating ID=40 CardID=",
        "tag=CONTROLLER value=2",
        "tag=ZONE value=HAND",
        "tag=ZONE_POSITION value=1",
        "TAG_CHANGE Entity=GameEntity tag=STEP value=BEGIN_MULLIGAN",
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=INPUT",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=INPUT",
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=DONE",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=DONE",
        "TAG_CHANGE Entity=GameEntity tag=TURN value=1",
        "TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_READY",
        "TAG_CHANGE Entity=GameEntity tag=TURN value=8",
        "TAG_CHANGE Entity=3 tag=PLAYSTATE value=LOST",
        "TAG_CHANGE Entity=2 tag=PLAYSTATE value=WON",
        "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE",
    ];

    private static List<string> CreateBattlegroundsFixture(bool includeMetadata)
    {
        var lines = new List<string>
        {
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
        };
        if (includeMetadata)
        {
            lines.Add($"{MetadataPrefix}BuildNumber=224857");
            lines.Add($"{MetadataPrefix}GameType=GT_BATTLEGROUNDS");
            lines.Add($"{MetadataPrefix}FormatType=FT_WILD");
        }

        lines.AddRange(
        [
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
            "BLOCK_START BlockType=ATTACK Entity=[name=Attacker id=201 zone=PLAY zonePos=1 cardId=BG_MINION_001 player=2] EffectCardId= EffectIndex=0 Target=0 SubOption=0",
            "BLOCK_END",
            "TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON",
        ]);
        return lines;
    }

    private sealed class CountingObserver : IAppliedMatchEventObserver
    {
        public int Started { get; private set; }

        public int Ended { get; private set; }

        public int Applied { get; private set; }

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) => Started++;

        public void OnEventApplied(IceCrow.Hearthstone.Protocol.Events.GameEvent gameEvent) => Applied++;

        public void OnEventRejected(IceCrow.Hearthstone.Protocol.Events.GameEvent gameEvent)
        {
        }

        public void OnMatchEnded(DateTimeOffset timestamp) => Ended++;
    }
}
