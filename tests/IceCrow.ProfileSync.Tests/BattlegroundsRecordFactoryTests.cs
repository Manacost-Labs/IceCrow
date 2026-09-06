using System.Globalization;
using System.Text.Json;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.ProfileSync.Factories;
using IceCrow.ProfileSync.Records;
using IceCrow.Tracking;

namespace IceCrow.ProfileSync.Tests;

public sealed class BattlegroundsRecordFactoryTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid MatchId = Guid.CreateVersion7();

    [Fact]
    public void ReturnsNullUntilTheSessionEndedWithAResult()
    {
        Assert.Null(BattlegroundsRecordFactory.Create(Snapshot(TrackingSessionState.Active, null), MatchId));
        Assert.Null(BattlegroundsRecordFactory.Create(Snapshot(TrackingSessionState.Ended, null), MatchId));
        Assert.Null(BattlegroundsRecordFactory.Create(Snapshot(TrackingSessionState.Active, Result()), MatchId));
        Assert.NotNull(BattlegroundsRecordFactory.Create(Snapshot(TrackingSessionState.Ended, Result()), MatchId));
    }

    [Fact]
    public void MapsASoloResultAndLeavesMmrUnknown()
    {
        var record = Create(Result());

        Assert.Equal(MatchId, record.MatchId);
        Assert.Equal(BattlegroundsMode.Solo, record.Mode);
        Assert.Null(record.MmrBefore);
        Assert.Null(record.MmrAfter);
        Assert.Equal(Certainty.Unknown, record.MmrConfidence);
        Assert.Equal("TB_BaconShop_HERO_41", record.HeroCardId);
        Assert.Equal(3, record.Placement);
        Assert.Equal(Certainty.Exact, record.PlacementConfidence);
        Assert.Equal(Timestamp, record.StartedAt);
        Assert.Equal(Timestamp.AddMinutes(20), record.EndedAt);
        Assert.Equal(1200, record.DurationSeconds);
        Assert.Equal(14, record.FinalTurn);
        Assert.Null(record.FinalBoard);
        Assert.Equal(224857, record.HearthstoneBuild);
    }

    [Theory]
    [InlineData("GT_BATTLEGROUNDS", BattlegroundsMode.Solo)]
    [InlineData("GT_BATTLEGROUNDS_FRIENDLY", BattlegroundsMode.Solo)]
    [InlineData("GT_BATTLEGROUNDS_DUO", BattlegroundsMode.Duos)]
    [InlineData("GT_RANKED", BattlegroundsMode.Unknown)]
    [InlineData(null, BattlegroundsMode.Unknown)]
    public void MapsTheModeFromTheGameTypeTokenWithoutGuessing(string? gameType, BattlegroundsMode expected)
    {
        var record = Create(Result(gameType: gameType, build: null));

        Assert.Equal(expected, record.Mode);
        Assert.Null(record.HearthstoneBuild);
    }

    [Theory]
    [InlineData(EvidenceCertainty.Exact, Certainty.Exact)]
    [InlineData(EvidenceCertainty.Partial, Certainty.Partial)]
    [InlineData(EvidenceCertainty.Inferred, Certainty.Inferred)]
    [InlineData(EvidenceCertainty.Unknown, Certainty.Unknown)]
    public void PlacementCertaintyMapsOneWayAndNeverUpward(EvidenceCertainty tracked, Certainty expected)
    {
        var record = Create(Result(placement: 5, placementCertainty: tracked));

        Assert.Equal(5, record.Placement);
        Assert.Equal(expected, record.PlacementConfidence);
    }

    [Fact]
    public void MissingPlacementIsUnknownEvenIfTrackingGradedIt()
    {
        var record = Create(Result(placement: null, placementCertainty: EvidenceCertainty.Exact));

        Assert.Null(record.Placement);
        Assert.Equal(Certainty.Unknown, record.PlacementConfidence);
    }

    [Fact]
    public void MapsTheFinalBoardWithSlotsStatsGoldenAndCertainty()
    {
        var board = Board(
            turn: 12,
            (401, 1, 5, 6, 1),
            (402, 2, 3, 4, 0),
            (403, 3, 1, 1, null));

        var record = Create(Result(finalTurn: 13, finalBoard: board, boardCertainty: EvidenceCertainty.Partial));

        var finalBoard = Assert.IsType<FinalBoardRecord>(record.FinalBoard);
        Assert.Equal(board.Timestamp, finalBoard.CapturedAt);
        Assert.Equal(12, finalBoard.Turn);
        Assert.Equal(Certainty.Partial, finalBoard.Confidence);
        Assert.Equal(
            [
                new FinalBoardMinion(1, "BG_401", 5, 6, true),
                new FinalBoardMinion(2, "BG_402", 3, 4, false),
                new FinalBoardMinion(3, "BG_403", 1, 1, null),
            ],
            finalBoard.Minions);
    }

    [Fact]
    public void ClampsDurationAndTurnsToTheRecordLimits()
    {
        var tooLong = Create(Result(finalTurn: 500, duration: TimeSpan.FromHours(10)));
        var reversed = Create(Result(duration: TimeSpan.FromMinutes(-5)));

        Assert.Equal(ProfileRecordLimits.MaximumDurationSeconds, tooLong.DurationSeconds);
        Assert.Equal(ProfileRecordLimits.MaximumTurns, tooLong.FinalTurn);
        Assert.Equal(0, reversed.DurationSeconds);
    }

    [Fact]
    public void OverlongCardIdsAreDroppedNotTruncated()
    {
        var overlong = new string('X', ProfileRecordLimits.MaximumCardIdLength + 1);
        var board = Board(12, overlong, (401, 1, 5, 6, null));

        var record = Create(Result(heroCardId: overlong, finalBoard: board, boardCertainty: EvidenceCertainty.Exact));

        Assert.Null(record.HeroCardId);
        Assert.Null(Assert.Single(record.FinalBoard!.Minions).CardId);
    }

    [Fact]
    public void RecordSerializesAsABattlegroundsMatchProfileEvent()
    {
        var board = Board(12, (401, 1, 5, 6, 1));
        var record = Create(Result(finalTurn: 12, finalBoard: board, boardCertainty: EvidenceCertainty.Exact));

        var profileEvent = ProfileEvent.Create(ProfileEventType.BattlegroundsMatch, record.EndedAt, record);

        var payload = profileEvent.Payload;
        Assert.Equal("solo", payload.GetProperty("mode").GetString());
        Assert.Equal("unknown", payload.GetProperty("mmrConfidence").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("mmrBefore").ValueKind);
        Assert.Equal("exact", payload.GetProperty("placementConfidence").GetString());
        var minion = Assert.Single(payload.GetProperty("finalBoard").GetProperty("minions").EnumerateArray());
        Assert.True(minion.GetProperty("isGolden").GetBoolean());
        Assert.Equal("exact", payload.GetProperty("finalBoard").GetProperty("confidence").GetString());
    }

    [Fact]
    public void MapsAResultFrozenByARealTrackingSession()
    {
        var session = new TrackingSession();
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: 1);
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.BuildNumber, "224857"));
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.GameType, "GT_BATTLEGROUNDS_DUO"));
        Tag(session, 1, "PLAYER_ID", "1");
        Tag(session, 1, "NEXT_OPPONENT_PLAYER_ID", "2");
        Tag(session, 2, "PLAYER_ID", "2");
        Tag(session, 101, "CARDTYPE", "HERO");
        Tag(session, 101, "PLAYER_ID", "1");
        _ = session.Apply(new EntityRevealed(Timestamp, null, 101, "Hero", "TB_BaconShop_HERO_41"));
        Tag(session, 301, "CARDTYPE", "MINION");
        Tag(session, 301, "ZONE", "PLAY");
        Tag(session, 301, "CONTROLLER", "1");
        Tag(session, 301, "ZONE_POSITION", "1");
        Tag(session, 301, "ATK", "4");
        Tag(session, 301, "HEALTH", "5");
        Tag(session, 301, "PREMIUM", "1");
        Tag(session, 500, "TURN", "3");
        Tag(session, 500, "2022", "1");
        Tag(session, 500, "2022", "0");
        _ = session.Apply(new BlockStarted(
            Timestamp.AddSeconds(30),
            new IceCrow.Hearthstone.Protocol.PowerBlock(1, null, 0, "ATTACK", 301, null, string.Empty, string.Empty, null, null)));
        Tag(session, 101, "PLAYER_LEADERBOARD_PLACE", "2");
        Tag(session, 1, "PLAYSTATE", "LOST");
        _ = session.EndMatch(Timestamp.AddMinutes(1));

        var record = Assert.IsType<BattlegroundsMatchRecord>(
            BattlegroundsRecordFactory.Create(session.Current, MatchId));

        Assert.Equal(BattlegroundsMode.Duos, record.Mode);
        Assert.Equal(224857, record.HearthstoneBuild);
        Assert.Equal("TB_BaconShop_HERO_41", record.HeroCardId);
        Assert.Equal(2, record.Placement);
        Assert.Equal(Certainty.Exact, record.PlacementConfidence);
        Assert.Equal(60, record.DurationSeconds);
        Assert.Equal(2, record.FinalTurn);
        var finalBoard = Assert.IsType<FinalBoardRecord>(record.FinalBoard);
        Assert.Equal(Certainty.Exact, finalBoard.Confidence);
        Assert.Equal(Timestamp.AddSeconds(30), finalBoard.CapturedAt);
        var minion = Assert.Single(finalBoard.Minions);
        Assert.Equal(new FinalBoardMinion(1, null, 4, 5, true), minion);
    }

    private static BattlegroundsMatchRecord Create(BattlegroundsMatchResult result) =>
        Assert.IsType<BattlegroundsMatchRecord>(
            BattlegroundsRecordFactory.Create(Snapshot(TrackingSessionState.Ended, result), MatchId));

    private static TrackingSnapshot Snapshot(TrackingSessionState state, BattlegroundsMatchResult? result) => new(
        Revision: 1,
        SessionState: state,
        StartedAt: Timestamp,
        EndedAt: result?.EndedAt,
        EntityCount: 0,
        TagCount: 0,
        MaximumTagsOnEntity: 0,
        TimelineEventCount: 0,
        MaximumTimelineEventsOnPlayer: 0,
        OpponentSnapshotCount: 0,
        MaximumOpponentSnapshotsOnPlayer: 0,
        Battlegrounds: BattlegroundsState.Empty,
        OpponentMemory: OpponentMemory.Empty,
        LobbyTimeline: LobbyTimelineSnapshot.Empty,
        Metadata: result?.Metadata ?? GameMetadataState.Empty,
        LocalBoard: result?.FinalBoard,
        Result: result);

    private static BattlegroundsMatchResult Result(
        int? placement = 3,
        EvidenceCertainty placementCertainty = EvidenceCertainty.Exact,
        string? heroCardId = "TB_BaconShop_HERO_41",
        int finalTurn = 14,
        BoardSnapshot? finalBoard = null,
        EvidenceCertainty boardCertainty = EvidenceCertainty.Unknown,
        string? gameType = "GT_BATTLEGROUNDS",
        int? build = 224857,
        TimeSpan? duration = null) => new(
            placement,
            placementCertainty,
            heroCardId,
            finalTurn,
            finalBoard,
            boardCertainty,
            Timestamp,
            Timestamp + (duration ?? TimeSpan.FromMinutes(20)),
            new GameMetadataState(build, gameType, null, null));

    private static BoardSnapshot Board(
        int turn,
        params (int EntityId, int Slot, int Attack, int Health, int? Premium)[] minions) =>
        Board(turn, "BG_", minions);

    private static BoardSnapshot Board(
        int turn,
        string cardIdPrefix,
        params (int EntityId, int Slot, int Attack, int Health, int? Premium)[] minions)
    {
        var store = new EntityStore();
        foreach (var (entityId, slot, attack, health, premium) in minions)
        {
            SetTag(store, entityId, "CARDTYPE", "MINION");
            SetTag(store, entityId, "ZONE", "PLAY");
            SetTag(store, entityId, "CONTROLLER", "1");
            SetTag(store, entityId, "ZONE_POSITION", slot.ToString(CultureInfo.InvariantCulture));
            SetTag(store, entityId, "ATK", attack.ToString(CultureInfo.InvariantCulture));
            SetTag(store, entityId, "HEALTH", health.ToString(CultureInfo.InvariantCulture));
            if (premium is int)
            {
                // The store only keeps a zero after a real transition, exactly
                // like the client printing a minion that stopped being golden.
                SetTag(store, entityId, "PREMIUM", "1");
                SetTag(store, entityId, "PREMIUM", premium.Value.ToString(CultureInfo.InvariantCulture));
            }

            _ = store.Apply(new EntityRevealed(
                Timestamp,
                null,
                entityId,
                $"Minion {entityId}",
                cardIdPrefix + entityId.ToString(CultureInfo.InvariantCulture)));
        }

        return BoardSnapshot.Capture(1, turn, Timestamp.AddMinutes(turn), store.CreateBoardSnapshots(1));
    }

    private static void SetTag(EntityStore store, int entityId, string tag, string value) =>
        _ = store.Apply(new RawTagChanged(Timestamp, null, entityId, null, tag, value, IsCreationTag: false));

    private static void Tag(TrackingSession session, int entityId, string tag, string value) =>
        _ = session.Apply(new RawTagChanged(Timestamp, null, entityId, null, tag, value, IsCreationTag: false));
}
