using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.ClientState;
using IceCrow.Live;
using IceCrow.ProfileSync;

namespace IceCrow.App.Tests;

/// <summary>
/// Long-session validation without restarting: menu, two ranked Standard
/// games, an Arena game, a Battlegrounds game, menu. Every finished match
/// becomes exactly one profile event, modes never leak into each other, and
/// the gameplay hold follows the active game.
/// </summary>
public sealed class ProfileRecordPipelineTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LongSessionProducesOneRecordPerFinishedMatchWithoutRestart()
    {
        var events = new List<ProfileEvent>();
        var holds = new List<bool>();
        var coordinator = new GameSessionCoordinator();
        var pipeline = new ProfileRecordPipeline(
            profileEvent =>
            {
                events.Add(profileEvent);
                return true;
            },
            holds.Add);
        var line = 0;

        void Feed(IEnumerable<string> payloads)
        {
            foreach (var payload in payloads)
            {
                var content = payload.StartsWith("GameState.DebugPrintGame()", StringComparison.Ordinal)
                    ? payload
                    : "PowerTaskList.DebugPrintPower() - " + payload;
                var update = coordinator.Process(new RawLogLine(Timestamp.AddSeconds(line++), "Power", content, content));
                pipeline.Observe(update, GameplayActive(coordinator));
            }
        }

        Feed(MenuNoise());
        Feed(RankedGame("FT_STANDARD", "WON"));
        Feed(RankedGame("FT_STANDARD", "LOST"));
        Feed(RankedGame("FT_WILD", "WON", gameType: "GT_ARENA"));
        Feed(BattlegroundsGame());
        Feed(MenuNoise());

        Assert.Equal(
            [ProfileEventType.ConstructedMatch, ProfileEventType.ConstructedMatch, ProfileEventType.ArenaMatch, ProfileEventType.BattlegroundsMatch],
            events.Select(static item => item.Type));
        Assert.Equal(events.Count, events.Select(static item => item.EventId).Distinct().Count());
        Assert.Equal(2, pipeline.ConstructedRecords);
        Assert.Equal(1, pipeline.ArenaRecords);
        Assert.Equal(1, pipeline.BattlegroundsRecords);
        Assert.Equal(0, pipeline.IgnoredCompletions);

        var first = events[0].Payload;
        Assert.Equal("standard", first.GetProperty("format").GetString());
        Assert.Equal("won", first.GetProperty("result").GetString());
        Assert.Equal("exact", first.GetProperty("resultConfidence").GetString());
        Assert.Equal("lost", events[1].Payload.GetProperty("result").GetString());

        var arena = events[2].Payload;
        Assert.False(arena.TryGetProperty("runId", out _));
        Assert.Equal("unknown", arena.GetProperty("scoreConfidence").GetString());

        var battlegrounds = events[3].Payload;
        Assert.Equal("solo", battlegrounds.GetProperty("mode").GetString());
        Assert.Equal(1, battlegrounds.GetProperty("placement").GetInt32());
        Assert.Equal("exact", battlegrounds.GetProperty("placementConfidence").GetString());
        Assert.Equal("unknown", battlegrounds.GetProperty("mmrConfidence").GetString());
        Assert.False(battlegrounds.TryGetProperty("mmrAfter", out _));

        // Hold: on for each game, off after each game. A Battlegrounds game
        // holds twice: once for the pre-metadata window (route Both) and once
        // for the confirmed session; both are released.
        Assert.Equal([true, false, true, false, true, false, true, false, true, false], holds);
        // The last game was Battlegrounds: the constructed tracker rejected it
        // (it stays formally open until the next boundary by design) and the
        // route remains Battlegrounds until the next CREATE_GAME.
        Assert.Equal(3, coordinator.Constructed.CompletedMatches);
        Assert.Equal(1, coordinator.Constructed.IgnoredModeGames);
        Assert.Equal(GameSessionRoute.Battlegrounds, coordinator.Route);
        Assert.False(GameplayActive(coordinator));
    }

    [Fact]
    public void ReplayedTerminalSnapshotDoesNotDuplicateTheBattlegroundsRecord()
    {
        var events = new List<ProfileEvent>();
        var coordinator = new GameSessionCoordinator();
        var pipeline = new ProfileRecordPipeline(
            profileEvent =>
            {
                events.Add(profileEvent);
                return true;
            },
            static _ => { });
        var line = 0;
        GameSessionUpdate? last = null;
        foreach (var payload in BattlegroundsGame())
        {
            var content = payload.StartsWith("GameState.DebugPrintGame()", StringComparison.Ordinal)
                ? payload
                : "PowerTaskList.DebugPrintPower() - " + payload;
            last = coordinator.Process(new RawLogLine(Timestamp.AddSeconds(line++), "Power", content, content));
            pipeline.Observe(last, false);
        }

        pipeline.Observe(last!, false);

        Assert.Single(events);
        Assert.Equal(1, pipeline.BattlegroundsRecords);
    }

    [Fact]
    public void ReplayingTheSameLogAfterRestartProducesStableMatchIdentities()
    {
        var first = ReplayFinishedMatches();
        var second = ReplayFinishedMatches();

        Assert.Equal(first.Select(static item => item.EventId), second.Select(static item => item.EventId));
        Assert.Equal(
            first.Select(static item => item.Payload.GetProperty("matchId").GetGuid()),
            second.Select(static item => item.Payload.GetProperty("matchId").GetGuid()));
        Assert.Equal(2, first.Count);
        Assert.Equal(2, first.Select(static item => item.EventId).Distinct().Count());
    }

    [Fact]
    public void DeckSelectionIsSnapshottedAtGameStart()
    {
        var events = new List<ProfileEvent>();
        var selection = new SelectedDeckSnapshot(
            Timestamp.AddMinutes(-1),
            "FIRST_DECK",
            null,
            "standard",
            []);
        var coordinator = new GameSessionCoordinator();
        var pipeline = new ProfileRecordPipeline(
            profileEvent =>
            {
                events.Add(profileEvent);
                return true;
            },
            static _ => { },
            getSelectedDeck: () => selection);
        var line = 0;
        foreach (var payload in RankedGame("FT_STANDARD", "WON"))
        {
            var content = payload.StartsWith("GameState.DebugPrintGame()", StringComparison.Ordinal)
                ? payload
                : "PowerTaskList.DebugPrintPower() - " + payload;
            var update = coordinator.Process(
                new RawLogLine(Timestamp.AddSeconds(line++), "Power", content, content));
            pipeline.Observe(update, GameplayActive(coordinator));
            selection = new SelectedDeckSnapshot(
                Timestamp.AddSeconds(line),
                "CHANGED_DURING_GAME",
                null,
                "standard",
                []);
        }

        var record = Assert.Single(events).Payload;
        Assert.Equal("FIRST_DECK", record.GetProperty("playerDeck").GetProperty("deckCode").GetString());
        Assert.Equal("inferred", record.GetProperty("playerDeck").GetProperty("confidence").GetString());
    }

    [Fact]
    public void BoundaryCompletionKeepsPreviousGamesDeckSelection()
    {
        var events = new List<ProfileEvent>();
        var selection = new SelectedDeckSnapshot(
            Timestamp.AddMinutes(-1),
            "FIRST_DECK",
            null,
            "standard",
            []);
        var coordinator = new GameSessionCoordinator();
        var pipeline = new ProfileRecordPipeline(
            profileEvent =>
            {
                events.Add(profileEvent);
                return true;
            },
            static _ => { },
            getSelectedDeck: () => selection);
        var line = 0;
        foreach (var payload in RankedGame("FT_STANDARD", "WON").SkipLast(1))
        {
            Process(payload);
        }

        selection = new SelectedDeckSnapshot(
            Timestamp.AddSeconds(line),
            "SECOND_DECK",
            null,
            "standard",
            []);
        Process("CREATE_GAME");

        var record = Assert.Single(events).Payload;
        Assert.Equal("FIRST_DECK", record.GetProperty("playerDeck").GetProperty("deckCode").GetString());

        void Process(string payload)
        {
            var content = payload.StartsWith("GameState.DebugPrintGame()", StringComparison.Ordinal)
                ? payload
                : "PowerTaskList.DebugPrintPower() - " + payload;
            var update = coordinator.Process(
                new RawLogLine(Timestamp.AddSeconds(line++), "Power", content, content));
            pipeline.Observe(update, GameplayActive(coordinator));
        }
    }

    private static List<ProfileEvent> ReplayFinishedMatches()
    {
        var events = new List<ProfileEvent>();
        var coordinator = new GameSessionCoordinator();
        var pipeline = new ProfileRecordPipeline(
            profileEvent =>
            {
                events.Add(profileEvent);
                return true;
            },
            static _ => { });
        var line = 0;
        foreach (var payload in RankedGame("FT_STANDARD", "WON").Concat(BattlegroundsGame()))
        {
            var content = payload.StartsWith("GameState.DebugPrintGame()", StringComparison.Ordinal)
                ? payload
                : "PowerTaskList.DebugPrintPower() - " + payload;
            var update = coordinator.Process(new RawLogLine(Timestamp.AddSeconds(line++), "Power", content, content));
            pipeline.Observe(update, GameplayActive(coordinator));
        }

        return events;
    }

    private static bool GameplayActive(GameSessionCoordinator coordinator) =>
        coordinator.Battlegrounds.CurrentSnapshot.SessionState == IceCrow.Tracking.TrackingSessionState.Active ||
        (coordinator.Route is GameSessionRoute.Constructed or GameSessionRoute.Both && coordinator.Constructed.IsGameOpen);

    private static IEnumerable<string> MenuNoise() =>
    [
        "PowerProcessor.EndCurrentTaskList()",
        "PowerProcessor.EndCurrentTaskList()",
    ];

    private static IEnumerable<string> RankedGame(string format, string localPlayState, string gameType = "GT_RANKED")
    {
        var opponentPlayState = localPlayState == "WON" ? "LOST" : "WON";
        return
        [
            "CREATE_GAME",
            "GameEntity EntityID=1",
            "tag=TURN value=1",
            "tag=ZONE value=PLAY",
            "tag=CARDTYPE value=GAME",
            "tag=STATE value=RUNNING",
            "Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115193835963207 lo=32799768]",
            "tag=PLAYSTATE value=PLAYING",
            "tag=PLAYER_ID value=1",
            "tag=ZONE value=PLAY",
            "tag=CONTROLLER value=1",
            "tag=CARDTYPE value=PLAYER",
            "Player EntityID=3 PlayerID=2 GameAccountId=[hi=144115193835963207 lo=41862246]",
            "tag=PLAYSTATE value=PLAYING",
            "tag=PLAYER_ID value=2",
            "tag=ZONE value=PLAY",
            "tag=CONTROLLER value=2",
            "tag=CARDTYPE value=PLAYER",
            "GameState.DebugPrintGame() - BuildNumber=224857",
            $"GameState.DebugPrintGame() - GameType={gameType}",
            $"GameState.DebugPrintGame() - FormatType={format}",
            "GameState.DebugPrintGame() - ScenarioID=2",
            "FULL_ENTITY - Creating ID=64 CardID=HERO_01",
            "tag=CONTROLLER value=1",
            "tag=CARDTYPE value=HERO",
            "tag=ZONE value=PLAY",
            "FULL_ENTITY - Creating ID=66 CardID=HERO_08",
            "tag=CONTROLLER value=2",
            "tag=CARDTYPE value=HERO",
            "tag=ZONE value=PLAY",
            "FULL_ENTITY - Creating ID=10 CardID=CS2_106",
            "tag=CONTROLLER value=1",
            "tag=CARDTYPE value=MINION",
            "tag=ZONE value=HAND",
            "tag=ZONE_POSITION value=1",
            "FULL_ENTITY - Creating ID=11 CardID=EX1_308",
            "tag=CONTROLLER value=1",
            "tag=CARDTYPE value=MINION",
            "tag=ZONE value=HAND",
            "tag=ZONE_POSITION value=2",
            "FULL_ENTITY - Creating ID=40 CardID=",
            "tag=ZONE value=HAND",
            "tag=CONTROLLER value=2",
            "tag=ZONE_POSITION value=1",
            "TAG_CHANGE Entity=GameEntity tag=STEP value=BEGIN_MULLIGAN",
            "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=INPUT",
            "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=INPUT",
            "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=DONE",
            "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=DONE",
            "TAG_CHANGE Entity=GameEntity tag=TURN value=2",
            "TAG_CHANGE Entity=GameEntity tag=TURN value=3",
            $"TAG_CHANGE Entity=2 tag=PLAYSTATE value={localPlayState}",
            $"TAG_CHANGE Entity=3 tag=PLAYSTATE value={opponentPlayState}",
            "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE",
        ];
    }

    private static IEnumerable<string> BattlegroundsGame() =>
    [
        "CREATE_GAME",
        "GameEntity EntityID=500",
        "GameState.DebugPrintGame() - BuildNumber=224857",
        "GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS",
        "GameState.DebugPrintGame() - FormatType=FT_WILD",
        "Player EntityID=1 PlayerID=1 GameAccountId=Account_1",
        "Player EntityID=2 PlayerID=2 GameAccountId=Account_2",
        "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2",
        "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=2",
        "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=3",
        "TAG_CHANGE Entity=500 tag=STEP value=BEGIN_MULLIGAN",
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
        "TAG_CHANGE Entity=1 tag=PLAYER_LEADERBOARD_PLACE value=1",
        "TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON",
        "TAG_CHANGE Entity=500 tag=STATE value=COMPLETE",
    ];
}
