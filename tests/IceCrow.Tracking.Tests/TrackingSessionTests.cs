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

        var combat = ApplyTag(session, 500, "2022", "0", 21);
        _ = ApplyTag(session, 201, "ATK", "9", 22);

        Assert.True(combat.EnteredCombat);
        var board = Assert.IsType<BoardSnapshot>(combat.ObservedBoard);
        Assert.Equal(2, board.PlayerId);
        Assert.Equal(2, board.Turn);
        Assert.Equal(7, Assert.Single(board.Minions).Attack);
        Assert.Same(board, session.Current.OpponentMemory.GetLatest(2));
        Assert.Single(session.Current.OpponentMemory.GetHistory(2)!.Snapshots);
        Assert.Equal(
            new OpponentObserved(2, 2, Timestamp.AddMilliseconds(21), 1),
            Assert.Single(session.Current.LobbyTimeline.Events.OfType<OpponentObserved>()));
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
}
