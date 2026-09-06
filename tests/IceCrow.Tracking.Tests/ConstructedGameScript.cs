using IceCrow.Hearthstone.Protocol;
using IceCrow.Tracking.Constructed;

namespace IceCrow.Tracking.Tests;

/// <summary>
/// Feeds realistic Power.log lines through the real parser into a
/// <see cref="ConstructedMatchTracker"/>. Entity ids follow the client's
/// layout: game entity 1, player entities 2 (PlayerID 1, local) and 3
/// (PlayerID 2), local deck 4-8, local hand 10+, opponent deck 34-38,
/// opponent hand 40+, heroes and hero powers 64-67, The Coin 68.
/// </summary>
internal sealed class ConstructedGameScript
{
    public const string PowerTaskListPrefix = "PowerTaskList.DebugPrintPower() - ";
    public const string GameStatePrefix = "GameState.DebugPrintPower() - ";
    public static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private const string MetadataPrefix = "GameState.DebugPrintGame() - ";

    private readonly PowerLineParser _parser = new();
    private int _lineIndex;

    public ConstructedGameScript(ConstructedMatchTracker? tracker = null)
    {
        Tracker = tracker ?? new ConstructedMatchTracker();
    }

    public ConstructedMatchTracker Tracker { get; }

    public List<ConstructedMatchSummary> Completed { get; } = [];

    public DateTimeOffset LastTimestamp { get; private set; }

    public ConstructedTrackerUpdate Feed(string payload, string prefix = PowerTaskListPrefix)
    {
        var content = payload.StartsWith(MetadataPrefix, StringComparison.Ordinal) ? payload : prefix + payload;
        LastTimestamp = Timestamp.AddMilliseconds(_lineIndex++);
        var parsed = _parser.Parse(content, LastTimestamp);
        Assert.Equal(PowerParseStatus.Parsed, parsed.Status);
        var update = Tracker.Apply(parsed.Event!);
        if (update.CompletedMatch is { } completed)
        {
            Completed.Add(completed);
        }

        return update;
    }

    public void Feed(IEnumerable<string> payloads, string prefix = PowerTaskListPrefix)
    {
        foreach (var payload in payloads)
        {
            _ = Feed(payload, prefix);
        }
    }

    public static IEnumerable<string> Concat(params IEnumerable<string>[] parts) =>
        parts.SelectMany(static part => part);

    public static IEnumerable<string> StandardGame(
        string gameType = "GT_RANKED",
        string formatType = "FT_STANDARD",
        string? localPlayState = "WON",
        string? opponentPlayState = "LOST",
        string build = "224857") => Concat(
            Header(),
            Metadata(gameType, formatType, build),
            LocalDeck(),
            OpponentDeck(),
            Heroes(),
            LocalHand("CS2_106", "EX1_308", "CS2_029"),
            OpponentHand(3),
            MulliganInput(),
            MulliganDone(),
            Turns(12),
            Finish(localPlayState, opponentPlayState));

    public static IEnumerable<string> Header(bool withTurn = true)
    {
        var lines = new List<string> { "CREATE_GAME", "GameEntity EntityID=1" };
        if (withTurn)
        {
            lines.Add("tag=TURN value=1");
        }

        lines.AddRange(
        [
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
        ]);
        return lines;
    }

    public static IEnumerable<string> Metadata(
        string gameType = "GT_RANKED",
        string formatType = "FT_STANDARD",
        string build = "224857") =>
    [
        $"{MetadataPrefix}BuildNumber={build}",
        $"{MetadataPrefix}GameType={gameType}",
        $"{MetadataPrefix}FormatType={formatType}",
        $"{MetadataPrefix}ScenarioID=2",
    ];

    public static IEnumerable<string> Heroes() =>
    [
        "FULL_ENTITY - Creating ID=64 CardID=HERO_01",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=HERO",
        "tag=ZONE value=PLAY",
        "tag=HEALTH value=30",
        "FULL_ENTITY - Creating ID=65 CardID=CS2_102",
        "tag=CONTROLLER value=1",
        "tag=CARDTYPE value=HERO_POWER",
        "tag=ZONE value=PLAY",
        "FULL_ENTITY - Creating ID=66 CardID=HERO_08",
        "tag=CONTROLLER value=2",
        "tag=CARDTYPE value=HERO",
        "tag=ZONE value=PLAY",
        "tag=HEALTH value=30",
        "FULL_ENTITY - Creating ID=67 CardID=CS2_034",
        "tag=CONTROLLER value=2",
        "tag=CARDTYPE value=HERO_POWER",
        "tag=ZONE value=PLAY",
    ];

    public static IEnumerable<string> LocalDeck() => HiddenCards(4, 5, controller: 1, zone: "DECK");

    public static IEnumerable<string> OpponentDeck() => HiddenCards(34, 5, controller: 2, zone: "DECK");

    public static IEnumerable<string> OpponentHand(int count) => HiddenCards(40, count, controller: 2, zone: "HAND");

    public static IEnumerable<string> HiddenCards(int firstId, int count, int controller, string zone)
    {
        var lines = new List<string>();
        for (var index = 0; index < count; index++)
        {
            lines.Add($"FULL_ENTITY - Creating ID={firstId + index} CardID=");
            lines.Add($"tag=ZONE value={zone}");
            lines.Add($"tag=CONTROLLER value={controller}");
            if (zone == "HAND")
            {
                lines.Add($"tag=ZONE_POSITION value={index + 1}");
            }
        }

        return lines;
    }

    public static IEnumerable<string> LocalHand(params string[] cardIds)
    {
        var lines = new List<string>();
        for (var index = 0; index < cardIds.Length; index++)
        {
            lines.Add($"FULL_ENTITY - Creating ID={10 + index} CardID={cardIds[index]}");
            lines.Add("tag=CONTROLLER value=1");
            lines.Add("tag=CARDTYPE value=MINION");
            lines.Add("tag=ZONE value=HAND");
            lines.Add($"tag=ZONE_POSITION value={index + 1}");
        }

        return lines;
    }

    public static IEnumerable<string> Coin(int controller, int zonePosition) =>
    [
        "FULL_ENTITY - Creating ID=68 CardID=GAME_005",
        $"tag=CONTROLLER value={controller}",
        "tag=CARDTYPE value=SPELL",
        "tag=ZONE value=HAND",
        $"tag=ZONE_POSITION value={zonePosition}",
    ];

    public static IEnumerable<string> MulliganInput() =>
    [
        "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId= EffectIndex=-1 Target=0 SubOption=-1 TriggerKeyword=0",
        "TAG_CHANGE Entity=GameEntity tag=STEP value=BEGIN_MULLIGAN",
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=INPUT",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=INPUT",
        "BLOCK_END",
    ];

    public static IEnumerable<string> MulliganDone() =>
    [
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=DEALING",
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=WAITING",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=DEALING",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=WAITING",
        "TAG_CHANGE Entity=2 tag=MULLIGAN_STATE value=DONE",
        "TAG_CHANGE Entity=3 tag=MULLIGAN_STATE value=DONE",
    ];

    /// <summary>The local player sends one hand card back and draws a hidden deck card in its place.</summary>
    public static IEnumerable<string> ReplaceLocal(
        int entityId,
        int replacementEntityId,
        string replacementCardId,
        int zonePosition) =>
    [
        $"TAG_CHANGE Entity={entityId} tag=ZONE value=DECK",
        $"SHOW_ENTITY - Updating Entity={replacementEntityId} CardID={replacementCardId}",
        "tag=CARDTYPE value=MINION",
        "tag=ZONE value=HAND",
        $"tag=ZONE_POSITION value={zonePosition}",
    ];

    public static IEnumerable<string> Turns(int last)
    {
        var lines = new List<string>();
        for (var turn = 2; turn <= last; turn++)
        {
            lines.Add($"TAG_CHANGE Entity=GameEntity tag=TURN value={turn}");
        }

        return lines;
    }

    public static IEnumerable<string> Reveal(
        int entityId,
        string cardId,
        string cardType = "SPELL",
        string zone = "PLAY") =>
    [
        $"SHOW_ENTITY - Updating Entity={entityId} CardID={cardId}",
        $"tag=CARDTYPE value={cardType}",
        $"tag=ZONE value={zone}",
    ];

    public static IEnumerable<string> Finish(
        string? localPlayState = "WON",
        string? opponentPlayState = "LOST",
        bool complete = true)
    {
        var lines = new List<string>();
        if (opponentPlayState is not null)
        {
            lines.Add($"TAG_CHANGE Entity=3 tag=PLAYSTATE value={opponentPlayState}");
        }

        if (localPlayState is not null)
        {
            lines.Add($"TAG_CHANGE Entity=2 tag=PLAYSTATE value={localPlayState}");
        }

        if (complete)
        {
            lines.Add("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");
            lines.Add("TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER");
        }

        return lines;
    }
}
