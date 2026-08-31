using System.Globalization;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Tests;

public sealed class TrackingSessionTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CombatEntryUpdatesOpponentMemoryAndTimelineExactlyOnce()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);

        // Combat entry only arms the capture — the enemy board is dealt after
        // the transition in the real client, so no board is observed yet.
        var combat = ApplyTag(session, 500, "2022", "0", 21);
        Assert.True(combat.EnteredCombat);
        Assert.Null(combat.ObservedBoard);

        var attack = ApplyAttackBlock(session, 22);
        _ = ApplyTag(session, 201, "ATK", "9", 23);
        var secondAttack = ApplyAttackBlock(session, 24);

        var board = Assert.IsType<BoardSnapshot>(attack.ObservedBoard);
        Assert.Null(secondAttack.ObservedBoard);
        Assert.Equal(2, board.PlayerId);
        Assert.Equal(2, board.Turn);
        Assert.Equal(7, Assert.Single(board.Minions).Attack);
        Assert.Same(board, session.Current.OpponentMemory.GetLatest(2));
        Assert.Single(session.Current.OpponentMemory.GetHistory(2)!.Snapshots);
        Assert.Equal(
            new OpponentObserved(2, 2, Timestamp.AddMilliseconds(22), 1),
            Assert.Single(session.Current.LobbyTimeline.Events.OfType<OpponentObserved>()));
    }

    [Fact]
    public void CombatWindowWithoutAnyAttackRecordsNoObservation()
    {
        // The real client also flips the compatibility phase tag during
        // shopping; a combat window that never produces an attack must record
        // no board observation instead of a false empty board.
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);
        _ = ApplyTag(session, 500, "2022", "0", 21);

        _ = ApplyTag(session, 500, "TURN", "5", 22);
        var lateAttack = ApplyAttackBlock(session, 23);

        Assert.Null(lateAttack.ObservedBoard);
        Assert.Empty(session.Current.OpponentMemory.Histories);
        Assert.Empty(session.Current.LobbyTimeline.Events.OfType<OpponentObserved>());
    }

    [Fact]
    public void ConsecutiveCombatsCaptureIndependentBoardsForDifferentOpponents()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);
        _ = ApplyTag(session, 500, "2022", "0", 21);
        var firstBoard = Assert.IsType<BoardSnapshot>(
            ApplyAttackBlock(session, 22).ObservedBoard);

        // Next round: the previous enemy board leaves play, a new opponent is
        // announced, and a different enemy minion is dealt for combat two.
        ApplyTag(session, 500, "TURN", "5", 30);
        ApplyTag(session, 201, "ZONE", "SETASIDE", 31);
        ApplyTag(session, 1, "NEXT_OPPONENT_PLAYER_ID", "3", 32);
        ApplyTag(session, 3, "PLAYER_ID", "3", 33);
        ApplyTag(session, 202, "CARDTYPE", "MINION", 34);
        ApplyTag(session, 202, "ZONE", "PLAY", 35);
        ApplyTag(session, 202, "CONTROLLER", "3", 36);
        ApplyTag(session, 202, "ZONE_POSITION", "1", 37);
        ApplyTag(session, 202, "ATK", "9", 38);
        ApplyTag(session, 500, "2022", "1", 39);
        _ = ApplyTag(session, 500, "2022", "0", 40);
        var secondBoard = Assert.IsType<BoardSnapshot>(
            ApplyAttackBlock(session, 41).ObservedBoard);

        Assert.Equal(2, firstBoard.PlayerId);
        Assert.Equal(3, secondBoard.PlayerId);
        Assert.Equal(9, Assert.Single(secondBoard.Minions).Attack);
        Assert.Single(session.Current.OpponentMemory.GetHistory(2)!.Snapshots);
        Assert.Single(session.Current.OpponentMemory.GetHistory(3)!.Snapshots);
        Assert.Equal(7, Assert.Single(firstBoard.Minions).Attack);
    }

    [Fact]
    public void PendingBoardCaptureDoesNotLeakIntoTheNextMatch()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);
        _ = ApplyTag(session, 500, "2022", "0", 21);

        _ = session.EndMatch(Timestamp.AddMilliseconds(22));
        _ = session.StartBattlegroundsMatch(Timestamp.AddMilliseconds(23), localPlayerId: 1);
        var attack = ApplyAttackBlock(session, 24);

        Assert.Null(attack.ObservedBoard);
        Assert.Empty(session.Current.OpponentMemory.Histories);
    }

    [Fact]
    public void CurrentSnapshotIsCachedAndEntitySnapshotsAreDetached()
    {
        var session = CreateRecruitSession();
        var current = session.Current;
        var captured = Assert.Single(
            session.CreateEntitySnapshots(),
            entity => entity.Id == 201);

        Assert.Same(current, session.Current);

        _ = ApplyTag(session, 201, "ATK", "11", 20);

        Assert.NotSame(current, session.Current);
        Assert.Equal(7, captured.Attack);
        Assert.Equal(11, Assert.Single(
            session.CreateEntitySnapshots(),
            entity => entity.Id == 201).Attack);
    }

    [Fact]
    public void TrackingSnapshotRemainsImmutableAfterLaterTimelineChanges()
    {
        var session = CreateRecruitSession();
        ApplyTag(session, 500, "2022", "1", 20);
        _ = ApplyTag(session, 500, "2022", "0", 21);
        var captured = session.Current;

        _ = ApplyTag(session, 2, "PLAYER_TECH_LEVEL", "4", 22);

        Assert.Equal(3, captured.Battlegrounds.Lobby.GetPlayer(2)?.TavernTier);
        Assert.Equal(
            [3],
            captured.LobbyTimeline.Events
                .OfType<TavernUpgraded>()
                .Select(static upgrade => upgrade.TavernTier));
        Assert.Equal(4, session.Current.Battlegrounds.Lobby.GetPlayer(2)?.TavernTier);
        Assert.Equal(
            [3, 4],
            session.Current.LobbyTimeline.Events
                .OfType<TavernUpgraded>()
                .Select(static upgrade => upgrade.TavernTier));
    }

    [Fact]
    public void EndingAndResettingClearTheAuthoritativeState()
    {
        var session = CreateRecruitSession();

        var ended = session.EndMatch(Timestamp.AddMinutes(1));

        Assert.Equal(TrackingSessionState.Ended, ended.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, session.Current.Battlegrounds.Phase);
        Assert.NotEmpty(session.Current.LobbyTimeline.Players);

        session.Reset();

        Assert.Equal(TrackingSessionState.Inactive, session.Current.SessionState);
        Assert.Equal(BattlegroundsState.Empty, session.Current.Battlegrounds);
        Assert.Empty(session.Current.OpponentMemory.Histories);
        Assert.Empty(session.Current.LobbyTimeline.Players);
        Assert.Empty(session.CreateEntitySnapshots());
    }

    [Fact]
    public void OptionalSessionEntityLimitIsIndependentFromReplayLimits()
    {
        var session = new TrackingSession(new TrackingSessionLimits(maximumTrackedEntities: 1));
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: 1);
        ApplyTag(session, 1, "PLAYER_ID", "1", 1);

        var exception = Assert.Throws<TrackingSafetyLimitExceededException>(
            () => ApplyTag(session, 2, "PLAYER_ID", "2", 2));
        Assert.Equal(TrackingSafetyLimit.TrackedEntities, exception.Limit);
    }

    private static TrackingSession CreateRecruitSession()
    {
        var session = new TrackingSession();
        _ = session.StartBattlegroundsMatch(Timestamp, localPlayerId: 1);
        ApplyTag(session, 1, "PLAYER_ID", "1", 1);
        ApplyTag(session, 1, "CURRENT_PLAYER", "1", 2);
        ApplyTag(session, 1, "NEXT_OPPONENT_PLAYER_ID", "2", 3);
        ApplyTag(session, 2, "PLAYER_ID", "2", 4);
        ApplyTag(session, 2, "PLAYER_TECH_LEVEL", "3", 5);
        ApplyTag(session, 201, "CARDTYPE", "MINION", 6);
        ApplyTag(session, 201, "ZONE", "PLAY", 7);
        ApplyTag(session, 201, "CONTROLLER", "2", 8);
        ApplyTag(session, 201, "ZONE_POSITION", "1", 9);
        ApplyTag(session, 201, "ATK", "7", 10);
        ApplyTag(session, 201, "HEALTH", "8", 11);
        ApplyTag(session, 500, "TURN", "3", 12);
        return session;
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
        int millisecond) => session.Apply(new IceCrow.Hearthstone.Protocol.Events.BlockStarted(
            Timestamp.AddMilliseconds(millisecond),
            new IceCrow.Hearthstone.Protocol.PowerBlock(
                Id: millisecond,
                ParentId: null,
                Depth: 0,
                Type: "ATTACK",
                EntityId: 201,
                EntityName: null,
                EffectCardId: string.Empty,
                Target: string.Empty,
                SubOption: null,
                TriggerKeyword: null)));
}
