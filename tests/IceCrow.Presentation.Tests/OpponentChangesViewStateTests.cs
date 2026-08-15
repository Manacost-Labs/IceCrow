using System.Globalization;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Presentation.Tests;

public sealed class OpponentChangesViewStateTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FactoryProducesNoChangesBeforeASecondFight()
    {
        var memory = CaptureBoards((Turn: 5, Minions: [(101, "BG_BRANN", 1, 4, 6)]));

        var tile = CreateOpponentTile(memory, currentTurn: 6);

        Assert.Null(tile.Changes);
        Assert.Null(tile.CompactChangeLine);
    }

    [Fact]
    public void FactoryComparesTheTwoMostRecentFightsOnly()
    {
        var memory = CaptureBoards(
            (Turn: 5, Minions: [(101, "BG_BRANN", 1, 1, 1)]),
            (Turn: 7, Minions: [(201, "BG_BRANN", 1, 2, 3), (202, "BG_IMP", 2, 1, 1)]),
            (Turn: 10, Minions: [(301, "BG_BRANN", 1, 8, 9), (305, "BG_MALCHEZAAR", 2, 9, 9)]));

        var changes = CreateOpponentTile(memory, currentTurn: 10).Changes;

        Assert.NotNull(changes);
        Assert.Equal(7, changes.PreviousTurn);
        Assert.Equal(10, changes.CurrentTurn);
        Assert.Equal("Previous · Turn 7", changes.PreviousSeenLine);
        Assert.Equal(3, changes.ChangeCount);
        Assert.Equal(
            [MinionChangeKind.Added, MinionChangeKind.Changed, MinionChangeKind.Removed],
            changes.Rows.Select(static row => row.Kind));
        Assert.Equal("BG_MALCHEZAAR", changes.Rows[0].DisplayName);
        Assert.Equal("9/9", changes.Rows[0].TransitionLine);
        Assert.Equal("2/3 → 8/9", changes.Rows[1].TransitionLine);
        Assert.Equal("+6/+6", changes.Rows[1].DeltaLine);
        Assert.Equal("BG_IMP", changes.Rows[2].DisplayName);
        Assert.Null(changes.NoChangeLine);
    }

    [Fact]
    public void IdenticalFightsReportAnUnchangedBoard()
    {
        var memory = CaptureBoards(
            (Turn: 5, Minions: [(101, "BG_BRANN", 1, 4, 6)]),
            (Turn: 8, Minions: [(201, "BG_BRANN", 1, 4, 6)]));

        var tile = CreateOpponentTile(memory, currentTurn: 8);

        Assert.NotNull(tile.Changes);
        Assert.Equal(0, tile.Changes.ChangeCount);
        Assert.Equal("Board unchanged", tile.Changes.NoChangeLine);
        Assert.Null(tile.CompactChangeLine);
        Assert.False(tile.Changes.IsMajorChange);
    }

    [Fact]
    public void LargeStatGrowthIsFlaggedAsAMajorChange()
    {
        var memory = CaptureBoards(
            (Turn: 5, Minions: [(101, "BG_BRANN", 1, 12, 18)]),
            (Turn: 8, Minions: [(201, "BG_BRANN", 1, 31, 42)]));

        var tile = CreateOpponentTile(memory, currentTurn: 8);

        Assert.NotNull(tile.Changes);
        Assert.True(tile.Changes.IsMajorChange);
        Assert.Equal("1 change", tile.CompactChangeLine);
    }

    [Fact]
    public void ChangedRowFormatsNegativeDeltasAsObserved()
    {
        var row = MinionChangeViewState.Changed("Brann", 10, 10, 6, 7);

        Assert.Equal("10/10 → 6/7", row.TransitionLine);
        Assert.Equal("−4/−3", row.DeltaLine);
        Assert.Equal(string.Empty, row.Marker);
    }

    [Fact]
    public void AddedAndRemovedRowsCarryTheirMarkers()
    {
        var added = MinionChangeViewState.Added("Malchezaar", 9, 9);
        var removed = MinionChangeViewState.Removed("Imp");

        Assert.Equal("+", added.Marker);
        Assert.Equal("9/9", added.TransitionLine);
        Assert.Null(added.DeltaLine);
        Assert.Equal("−", removed.Marker);
        Assert.Null(removed.TransitionLine);
    }

    [Fact]
    public void UnknownCardsFallBackToCardIdAndThenEntityId()
    {
        var memory = CaptureBoards(
            (Turn: 5, Minions: [(101, "BG_BRANN", 1, 1, 1)]),
            (Turn: 8, Minions: [(201, "BG_BRANN", 1, 1, 1), (205, null, 2, 3, 3)]));

        var changes = CreateOpponentTile(memory, currentTurn: 8).Changes;

        Assert.NotNull(changes);
        var addedRow = Assert.Single(changes.Rows);
        Assert.Equal("Entity 205", addedRow.DisplayName);
    }

    [Theory]
    [InlineData(0, OpponentStaleness.Fresh)]
    [InlineData(1, OpponentStaleness.Recent)]
    [InlineData(2, OpponentStaleness.Recent)]
    [InlineData(3, OpponentStaleness.Stale)]
    [InlineData(5, OpponentStaleness.Stale)]
    [InlineData(6, OpponentStaleness.VeryStale)]
    [InlineData(11, OpponentStaleness.VeryStale)]
    public void StalenessFollowsTheBoardAgeInTurns(int turnsAgo, OpponentStaleness expected)
    {
        var tile = CreateViewState(turnsAgo: turnsAgo);

        Assert.Equal(expected, tile.Staleness);
        Assert.Equal(
            expected is OpponentStaleness.Stale or OpponentStaleness.VeryStale,
            tile.IsStale);
    }

    [Fact]
    public void NeverFoughtOpponentsHaveNoStaleness()
    {
        var tile = new OpponentOverlayViewState(
            playerId: 4,
            heroName: "Player 4",
            heroCardId: null,
            tavernTier: 0,
            health: 30,
            armor: 0,
            presence: OpponentPresence.NotFought,
            lastSeenTurn: null,
            turnsAgo: null,
            triples: 0,
            progressionRows: [],
            board: []);

        Assert.Null(tile.Staleness);
    }

    [Fact]
    public void EqualChangeViewStatesCompareEqual()
    {
        Assert.Equal(CreateChanges(), CreateChanges());
        Assert.Equal(CreateChanges().GetHashCode(), CreateChanges().GetHashCode());
        Assert.Equal(CreateViewState(changes: CreateChanges()), CreateViewState(changes: CreateChanges()));
    }

    [Fact]
    public void DifferentChangeRowsMakeTheViewStateDifferent()
    {
        var withGrowth = new OpponentChangesViewState(
            7,
            10,
            isMajorChange: false,
            [MinionChangeViewState.Changed("Brann", 2, 3, 8, 9)]);

        Assert.NotEqual(CreateChanges(), withGrowth);
        Assert.NotEqual(
            CreateViewState(changes: CreateChanges()),
            CreateViewState(changes: withGrowth));
        Assert.NotEqual(CreateViewState(changes: CreateChanges()), CreateViewState(changes: null));
    }

    private static OpponentChangesViewState CreateChanges() => new(
        7,
        10,
        isMajorChange: false,
        [MinionChangeViewState.Added("Malchezaar", 9, 9)]);

    private static OpponentOverlayViewState CreateViewState(
        int turnsAgo = 1,
        OpponentChangesViewState? changes = null) => new(
            playerId: 2,
            heroName: "Reno Jackson",
            heroCardId: "TB_BaconShop_HERO_41",
            tavernTier: 5,
            health: 28,
            armor: 0,
            presence: OpponentPresence.Seen,
            lastSeenTurn: 8,
            turnsAgo: turnsAgo,
            triples: 1,
            progressionRows: ["T2·3"],
            board: [MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null)],
            changes: changes);

    private static OpponentOverlayViewState CreateOpponentTile(
        OpponentMemory memory,
        int currentTurn)
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var opponent = LobbyPlayer.Create(2) with { HeroName = "Reno Jackson", Health = 28 };
        var state = new BattlegroundsState(
            IsActive: true,
            Turn: currentTurn,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: local.PlayerId,
            CurrentOpponentPlayerId: opponent.PlayerId,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(opponent));
        var snapshot = new TrackingSnapshot(
            Revision: 1,
            SessionState: TrackingSessionState.Active,
            StartedAt: DateTimeOffset.UnixEpoch,
            EndedAt: null,
            EntityCount: 0,
            TagCount: 0,
            MaximumTagsOnEntity: 0,
            TimelineEventCount: 0,
            MaximumTimelineEventsOnPlayer: 0,
            OpponentSnapshotCount: 0,
            MaximumOpponentSnapshotsOnPlayer: 0,
            Battlegrounds: state,
            OpponentMemory: memory,
            LobbyTimeline: LobbyTimelineSnapshot.Empty);

        return Assert.Single(BattlegroundsOverlayViewStateFactory.Create(snapshot).Opponents);
    }

    private static OpponentMemory CaptureBoards(
        params (int Turn, (int EntityId, string? CardId, int ZonePosition, int Attack, int Health)[] Minions)[] boards)
    {
        var service = new OpponentMemoryService();
        foreach (var (turn, minions) in boards)
        {
            // A fresh store per fight mirrors Battlegrounds recreating the
            // opponent warband entities for every combat.
            var store = new EntityStore();
            foreach (var (entityId, cardId, zonePosition, attack, health) in minions)
            {
                SetTag(store, entityId, "CARDTYPE", "MINION");
                SetTag(store, entityId, "ZONE", "PLAY");
                SetTag(store, entityId, "CONTROLLER", "2");
                SetTag(
                    store,
                    entityId,
                    "ZONE_POSITION",
                    zonePosition.ToString(CultureInfo.InvariantCulture));
                SetTag(store, entityId, "ATK", attack.ToString(CultureInfo.InvariantCulture));
                SetTag(store, entityId, "HEALTH", health.ToString(CultureInfo.InvariantCulture));
                if (cardId is not null)
                {
                    _ = store.Apply(new EntityRevealed(
                        Timestamp,
                        BlockId: null,
                        EntityId: entityId,
                        EntityName: $"Minion {entityId}",
                        CardId: cardId));
                }
            }

            _ = service.Capture(2, turn, store.CreateAllSnapshots(), Timestamp.AddMinutes(turn));
        }

        return service.Memory;
    }

    private static void SetTag(EntityStore store, int entityId, string tag, string value) =>
        _ = store.Apply(new RawTagChanged(
            Timestamp,
            BlockId: null,
            EntityId: entityId,
            EntityName: null,
            Tag: tag,
            Value: value,
            IsCreationTag: false));
}
