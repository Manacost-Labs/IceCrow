using System.Globalization;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Tests;

/// <summary>
/// The Battlegrounds result facts: the placement the client prints on the
/// local hero, the local warband frozen at the first attack of its last
/// fight, and the metadata block — each carrying the certainty its evidence
/// supports and nothing more.
/// </summary>
public sealed class BattlegroundsResultTests
{
    private const int LocalPlayerId = 1;
    private const int LocalHeroId = 101;

    // The real client deals the enemy board to the local slot + 8.
    private const int OpposingController = LocalPlayerId + 8;

    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlacementObservedOnTheLocalHeroIsExact()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, LocalHeroId, "PLAYER_LEADERBOARD_PLACE", "3", 20);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 21);

        var snapshot = EndMatch(session, 22);

        var result = Assert.IsType<BattlegroundsMatchResult>(snapshot.Result);
        Assert.Equal(3, result.Placement);
        Assert.Equal(EvidenceCertainty.Exact, result.PlacementCertainty);
        Assert.Equal(3, snapshot.Battlegrounds.LocalPlacement);
        Assert.Equal("TB_BaconShop_HERO_41", result.HeroCardId);
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
    }

    [Fact]
    public void PlacementNeverObservedStaysNullAndUnknown()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 20);

        var result = Assert.IsType<BattlegroundsMatchResult>(EndMatch(session, 21).Result);

        Assert.Null(result.Placement);
        Assert.Equal(EvidenceCertainty.Unknown, result.PlacementCertainty);
    }

    [Fact]
    public void PlacementPrintedAfterTheTerminalPlaystateIsStillFrozenIntoTheResult()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 20);
        ApplyTag(session, LocalHeroId, "PLAYER_LEADERBOARD_PLACE", "6", 21);

        var result = Assert.IsType<BattlegroundsMatchResult>(EndMatch(session, 22).Result);

        Assert.Equal(6, result.Placement);
        Assert.Equal(EvidenceCertainty.Exact, result.PlacementCertainty);
    }

    [Theory]
    [InlineData("GT_BATTLEGROUNDS", 8, EvidenceCertainty.Exact)]
    [InlineData("GT_BATTLEGROUNDS", 9, EvidenceCertainty.Partial)]
    [InlineData("GT_BATTLEGROUNDS_DUO", 4, EvidenceCertainty.Exact)]
    [InlineData("GT_BATTLEGROUNDS_DUO", 5, EvidenceCertainty.Partial)]
    public void PlacementBeyondTheLobbySizeOfTheModeIsKeptButPartial(
        string gameType,
        int place,
        EvidenceCertainty expected)
    {
        var session = CreateRecruitSession();
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.GameType, gameType));
        ApplyTag(session, LocalHeroId, "PLAYER_LEADERBOARD_PLACE", place.ToString(CultureInfo.InvariantCulture), 20);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 21);

        var result = Assert.IsType<BattlegroundsMatchResult>(EndMatch(session, 22).Result);

        Assert.Equal(place, result.Placement);
        Assert.Equal(expected, result.PlacementCertainty);
    }

    [Fact]
    public void LocalBoardIsCapturedAtTheFirstAttackNotAtCombatEntry()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);
        var combat = ApplyTag(session, 500, "2022", "0", 21);
        Assert.True(combat.EnteredCombat);
        Assert.Null(combat.ObservedLocalBoard);
        Assert.Null(session.Current.LocalBoard);

        var attack = ApplyAttackBlock(session, 22);

        var board = Assert.IsType<BoardSnapshot>(attack.ObservedLocalBoard);
        Assert.Equal(LocalPlayerId, board.PlayerId);
        Assert.Equal(2, board.Turn);
        Assert.Equal(Timestamp.AddMilliseconds(22), board.Timestamp);
        Assert.Equal([301, 302], board.Minions.Select(static minion => minion.EntityId));
        Assert.Equal([1, 2], board.Minions.Select(static minion => minion.ZonePosition));
        Assert.Equal(["BG_MINION_301", "BG_MINION_302"], board.Minions.Select(static minion => minion.CardId));
        Assert.Same(board, session.Current.LocalBoard);

        // The opponent board is taken at the same instant and stays separate.
        var opponentBoard = Assert.IsType<BoardSnapshot>(attack.ObservedBoard);
        Assert.Equal(2, opponentBoard.PlayerId);
        Assert.Equal(201, Assert.Single(opponentBoard.Minions).EntityId);
    }

    [Fact]
    public void SecondCombatWindowOfTheSameRoundKeepsTheEnteringLocalBoard()
    {
        var session = CreateRecruitSession();
        var entering = CaptureCombat(session, 20);

        ApplyTag(session, 500, "TURN", "4", 30);
        ApplyTag(session, 301, "ATK", "1", 31);
        ApplyTag(session, 500, "2022", "1", 32);
        _ = ApplyTag(session, 500, "2022", "0", 33);
        var remnantAttack = ApplyAttackBlock(session, 34);

        Assert.Null(remnantAttack.ObservedLocalBoard);
        Assert.Null(remnantAttack.ObservedBoard);
        Assert.Same(entering, session.Current.LocalBoard);
        Assert.Equal(5, entering.Minions[0].Attack);
    }

    [Fact]
    public void LocalBoardIsBoundedToTheSevenRealSlots()
    {
        var session = CreateRecruitSession();
        for (var slot = 3; slot <= 7; slot++)
        {
            ApplyMinion(session, 300 + slot, LocalPlayerId, slot, attack: slot, health: slot, 13);
        }

        // Slot residue beyond the board and a stale duplicate in slot 1.
        ApplyMinion(session, 308, LocalPlayerId, zonePosition: 8, attack: 1, health: 1, 14);
        ApplyMinion(session, 309, LocalPlayerId, zonePosition: 0, attack: 1, health: 1, 15);
        ApplyMinion(session, 310, LocalPlayerId, zonePosition: 1, attack: 9, health: 9, 16);

        var board = CaptureCombat(session, 20);

        Assert.Equal(7, board.Minions.Count);
        Assert.Equal(Enumerable.Range(1, 7), board.Minions.Select(static minion => minion.ZonePosition));
        Assert.Equal(310, board.Minions[0].EntityId);
    }

    [Fact]
    public void FinalBoardIsExactWhenTheMatchEndsInTheCapturedCombat()
    {
        var session = CreateRecruitSession();
        var board = CaptureCombat(session, 20);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 30);

        var snapshot = EndMatch(session, 31);

        var result = Assert.IsType<BattlegroundsMatchResult>(snapshot.Result);
        Assert.Same(board, result.FinalBoard);
        Assert.Same(board, snapshot.LocalBoard);
        Assert.Equal(2, result.FinalTurn);
        Assert.Equal(EvidenceCertainty.Exact, result.FinalBoardCertainty);
    }

    [Fact]
    public void FinalBoardIsPartialWhenTheMatchEndsDuringALaterRecruitPhase()
    {
        var session = CreateRecruitSession();
        var board = CaptureCombat(session, 20);
        ApplyTag(session, 500, "TURN", "5", 30);
        ApplyTag(session, 301, "ATK", "12", 31);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "CONCEDED", 32);

        var result = Assert.IsType<BattlegroundsMatchResult>(EndMatch(session, 33).Result);

        Assert.Same(board, result.FinalBoard);
        Assert.Equal(3, result.FinalTurn);
        Assert.Equal(2, board.Turn);
        Assert.Equal(EvidenceCertainty.Partial, result.FinalBoardCertainty);
        Assert.Equal(5, board.Minions[0].Attack);
    }

    [Fact]
    public void FinalBoardIsUnknownWhenNoCombatHappened()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "CONCEDED", 20);

        var snapshot = EndMatch(session, 21);

        var result = Assert.IsType<BattlegroundsMatchResult>(snapshot.Result);
        Assert.Null(result.FinalBoard);
        Assert.Null(snapshot.LocalBoard);
        Assert.Equal(EvidenceCertainty.Unknown, result.FinalBoardCertainty);
    }

    [Fact]
    public void GameOverCleanupDoesNotAlterTheFrozenBoard()
    {
        var session = CreateRecruitSession();
        var board = CaptureCombat(session, 20);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 30);
        Assert.Equal(BattlegroundsPhase.GameOver, session.Current.Battlegrounds.Phase);

        // Wrap-up churn after the terminal playstate: a minion leaves play,
        // stats, slots and premium flip, and a new minion appears on the
        // local side.
        ApplyTag(session, 301, "ZONE", "GRAVEYARD", 31);
        ApplyTag(session, 302, "ATK", "99", 32);
        ApplyTag(session, 302, "ZONE_POSITION", "5", 33);
        ApplyTag(session, 302, "PREMIUM", "1", 34);
        ApplyMinion(session, 303, LocalPlayerId, zonePosition: 1, attack: 1, health: 1, 35);
        Assert.Same(board, session.Current.LocalBoard);

        var snapshot = EndMatch(session, 40);

        var result = Assert.IsType<BattlegroundsMatchResult>(snapshot.Result);
        Assert.Same(board, result.FinalBoard);
        Assert.Equal([301, 302], board.Minions.Select(static minion => minion.EntityId));
        Assert.Equal(5, board.Minions[0].Attack);
        Assert.Equal(3, board.Minions[1].Attack);
        Assert.Equal(2, board.Minions[1].ZonePosition);
        Assert.Null(board.Minions[1].IsGolden);

        // Nothing reaches the ended session, and the next match resets the
        // entity store without touching the frozen result.
        Assert.Throws<InvalidOperationException>(() => ApplyTag(session, 302, "ATK", "1", 41));
        _ = session.StartBattlegroundsMatch(Timestamp.AddMinutes(5), localPlayerId: LocalPlayerId);
        Assert.Empty(session.CreateEntitySnapshots());
        Assert.Same(board, snapshot.Result?.FinalBoard);
        Assert.Null(session.Current.Result);
        Assert.Null(session.Current.LocalBoard);
    }

    [Fact]
    public void GoldenMinionsAreFlaggedOnlyWhenPremiumWasObserved()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 301, "PREMIUM", "1", 13);
        ApplyTag(session, 302, "PREMIUM", "1", 14);
        ApplyTag(session, 302, "PREMIUM", "0", 15);
        ApplyMinion(session, 303, LocalPlayerId, zonePosition: 3, attack: 1, health: 1, 16);

        var board = CaptureCombat(session, 20);

        Assert.Equal([true, false, null], board.Minions.Select(static minion => minion.IsGolden));
    }

    [Fact]
    public void MetadataReachesTheSnapshotAndTheFrozenResult()
    {
        var session = CreateRecruitSession();
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.BuildNumber, "224857"));
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.GameType, "GT_BATTLEGROUNDS_DUO"));
        Assert.Equal(224857, session.Current.Metadata.BuildNumber);
        Assert.Equal(GameMode.BattlegroundsDuo, session.Current.Metadata.Mode);

        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 20);
        var result = Assert.IsType<BattlegroundsMatchResult>(EndMatch(session, 21).Result);

        Assert.Equal(GameMode.BattlegroundsDuo, result.Metadata.Mode);
        Assert.Equal(224857, result.Metadata.BuildNumber);
        Assert.Equal(Timestamp, result.StartedAt);
        Assert.Equal(Timestamp.AddMilliseconds(21), result.EndedAt);
    }

    [Fact]
    public void ResetClearsTheResultBoardMetadataAndPlacementForTheNextMatch()
    {
        var session = CreateRecruitSession();
        _ = session.Apply(new GameMetadataObserved(Timestamp, GameMetadataField.GameType, "GT_BATTLEGROUNDS"));
        _ = CaptureCombat(session, 20);
        ApplyTag(session, LocalHeroId, "PLAYER_LEADERBOARD_PLACE", "2", 30);
        ApplyTag(session, LocalPlayerId, "PLAYSTATE", "LOST", 31);
        Assert.NotNull(EndMatch(session, 32).Result);

        session.Reset();

        Assert.Equal(TrackingSessionState.Inactive, session.Current.SessionState);
        Assert.Null(session.Current.Result);
        Assert.Null(session.Current.LocalBoard);
        Assert.Equal(GameMetadataState.Empty, session.Current.Metadata);
        Assert.Null(session.Current.Battlegrounds.LocalPlacement);
    }

    private static TrackingSession CreateRecruitSession()
    {
        var session = new TrackingSession();
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: LocalPlayerId);
        ApplyTag(session, 1, "PLAYER_ID", "1", 1);
        ApplyTag(session, 1, "CURRENT_PLAYER", "1", 2);
        ApplyTag(session, 1, "HERO_ENTITY", "101", 3);
        ApplyTag(session, 1, "NEXT_OPPONENT_PLAYER_ID", "2", 4);
        ApplyTag(session, 2, "PLAYER_ID", "2", 5);
        ApplyTag(session, 2, "HERO_ENTITY", "102", 6);
        ApplyHero(session, LocalHeroId, playerId: 1, "TB_BaconShop_HERO_41", 7);
        ApplyHero(session, 102, playerId: 2, "TB_BaconShop_HERO_49", 8);
        ApplyMinion(session, 301, LocalPlayerId, zonePosition: 1, attack: 5, health: 6, 9);
        ApplyMinion(session, 302, LocalPlayerId, zonePosition: 2, attack: 3, health: 4, 10);
        ApplyMinion(session, 201, OpposingController, zonePosition: 1, attack: 7, health: 8, 11);
        ApplyTag(session, 500, "TURN", "3", 12);
        return session;
    }

    private static BoardSnapshot CaptureCombat(TrackingSession session, int millisecond)
    {
        ApplyTag(session, 500, "2022", "1", millisecond);
        _ = ApplyTag(session, 500, "2022", "0", millisecond + 1);
        return Assert.IsType<BoardSnapshot>(
            ApplyAttackBlock(session, millisecond + 2).ObservedLocalBoard);
    }

    private static TrackingSnapshot EndMatch(TrackingSession session, int millisecond)
    {
        _ = session.EndMatch(Timestamp.AddMilliseconds(millisecond));
        return session.Current;
    }

    private static void ApplyHero(
        TrackingSession session,
        int entityId,
        int playerId,
        string cardId,
        int millisecond)
    {
        var player = playerId.ToString(CultureInfo.InvariantCulture);
        ApplyTag(session, entityId, "CARDTYPE", "HERO", millisecond);
        ApplyTag(session, entityId, "PLAYER_ID", player, millisecond);
        ApplyTag(session, entityId, "CONTROLLER", player, millisecond);
        ApplyTag(session, entityId, "ZONE", "PLAY", millisecond);
        _ = session.Apply(new EntityRevealed(
            Timestamp.AddMilliseconds(millisecond),
            BlockId: null,
            EntityId: entityId,
            EntityName: $"Hero {entityId}",
            CardId: cardId));
    }

    private static void ApplyMinion(
        TrackingSession session,
        int entityId,
        int controller,
        int zonePosition,
        int attack,
        int health,
        int millisecond)
    {
        var culture = CultureInfo.InvariantCulture;
        ApplyTag(session, entityId, "CARDTYPE", "MINION", millisecond);
        ApplyTag(session, entityId, "ZONE", "PLAY", millisecond);
        ApplyTag(session, entityId, "CONTROLLER", controller.ToString(culture), millisecond);
        ApplyTag(session, entityId, "ZONE_POSITION", zonePosition.ToString(culture), millisecond);
        ApplyTag(session, entityId, "ATK", attack.ToString(culture), millisecond);
        ApplyTag(session, entityId, "HEALTH", health.ToString(culture), millisecond);
        _ = session.Apply(new EntityRevealed(
            Timestamp.AddMilliseconds(millisecond),
            BlockId: null,
            EntityId: entityId,
            EntityName: $"Minion {entityId}",
            CardId: $"BG_MINION_{entityId}"));
    }

    private static TrackingUpdate ApplyTag(
        TrackingSession session,
        int entityId,
        string tag,
        string value,
        int millisecond) => session.Apply(new RawTagChanged(
            Timestamp.AddMilliseconds(millisecond),
            BlockId: null,
            EntityId: entityId,
            EntityName: null,
            Tag: tag,
            Value: value,
            IsCreationTag: false));

    private static TrackingUpdate ApplyAttackBlock(
        TrackingSession session,
        int millisecond) => session.Apply(new BlockStarted(
            Timestamp.AddMilliseconds(millisecond),
            new IceCrow.Hearthstone.Protocol.PowerBlock(
                Id: millisecond,
                ParentId: null,
                Depth: 0,
                Type: "ATTACK",
                EntityId: 301,
                EntityName: null,
                EffectCardId: string.Empty,
                Target: string.Empty,
                SubOption: null,
                TriggerKeyword: null)));
}
