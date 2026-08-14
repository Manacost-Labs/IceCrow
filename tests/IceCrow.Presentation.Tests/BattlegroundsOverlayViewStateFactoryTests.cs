using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Presentation;
using IceCrow.Tracking;

namespace IceCrow.Presentation.Tests;

public sealed class BattlegroundsOverlayViewStateFactoryTests
{
    [Fact]
    public void CreateMapsLobbyWithoutExposingDomainObjectsToOverlay()
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var opponent = LobbyPlayer.Create(2) with
        {
            HeroName = "Reno",
            HeroCardId = "TB_BaconShop_HERO_41",
            TavernTier = 4,
            Triples = 2,
        };
        var state = new BattlegroundsState(
            IsActive: true,
            Turn: 8,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: 1,
            CurrentOpponentPlayerId: 2,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(opponent));
        var snapshot = CreateSnapshot(state);

        var result = BattlegroundsOverlayViewStateFactory.Create(snapshot);

        Assert.True(result.ShowLobby);
        var tile = Assert.Single(result.Opponents);
        Assert.Equal(2, tile.PlayerId);
        Assert.Equal("Reno", tile.HeroDisplay);
        Assert.Equal("Tavern Tier: 4", tile.TavernTierLine);
        Assert.Equal("Triples: 2", tile.TriplesLine);
        Assert.Equal("NOT FOUGHT YET", tile.StatusLine);
        Assert.Equal(["NOT FOUGHT YET"], tile.BoardRows);
    }

    [Fact]
    public void ViewStateCopiesCallerOwnedCollections()
    {
        var progression = new List<string> { "T2 -> Turn 3" };
        var board = new List<string> { "1. MINION 3/4" };
        var opponent = new OpponentOverlayViewState(
            2,
            "Reno",
            null,
            "Tavern Tier: 2",
            "Last Seen: Turn 3",
            "Last Seen: Turn 3",
            "Age: 1 turns",
            progression,
            "Triples: 0",
            board);
        var opponents = new List<OpponentOverlayViewState> { opponent };
        var viewState = new BattlegroundsOverlayViewState(true, opponents);

        progression.Add("T3 -> Turn 5");
        board.Clear();
        opponents.Clear();

        Assert.Single(viewState.Opponents);
        Assert.Equal(["T2 -> Turn 3"], opponent.ProgressionRows);
        Assert.Equal(["1. MINION 3/4"], opponent.BoardRows);
    }

    private static TrackingSnapshot CreateSnapshot(BattlegroundsState state) => new(
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
        OpponentMemory: OpponentMemory.Empty,
        LobbyTimeline: LobbyTimelineSnapshot.Empty);
}
