using System.Globalization;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Presentation.Tests;

public sealed class BattlegroundsOverlayViewStateFactoryTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateMapsLobbyWithoutExposingDomainObjectsToOverlay()
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var opponent = LobbyPlayer.Create(2) with
        {
            HeroName = "Reno Jackson",
            HeroCardId = "TB_BaconShop_HERO_41",
            TavernTier = 4,
            Health = 28,
            Triples = 2,
        };
        var snapshot = CreateSnapshot(CreateState(local, opponent));

        var result = BattlegroundsOverlayViewStateFactory.Create(snapshot);

        Assert.True(result.ShowLobby);
        var tile = Assert.Single(result.Opponents);
        Assert.Equal(2, tile.PlayerId);
        Assert.Equal("Reno Jackson", tile.HeroName);
        Assert.Equal("T4", tile.TierBadge);
        Assert.Equal("28 HP", tile.HealthLine);
        Assert.Equal("2 triples", tile.TriplesLine);
        Assert.Equal(OpponentPresence.NotFought, tile.Presence);
        Assert.Equal("Not fought yet", tile.LastSeenLine);
        Assert.Equal("—", tile.AgeBadge);
        Assert.Empty(tile.Board);
        Assert.Equal("Not fought yet", tile.BoardPlaceholder);
        Assert.True(tile.RequiresAttention);
    }

    [Fact]
    public void CreateProjectsRecordedBoardIntoMinionTiles()
    {
        var opponent = LobbyPlayer.Create(2) with
        {
            HeroName = "Reno Jackson",
            TavernTier = 5,
            Health = 21,
            Armor = 5,
        };
        var memory = CaptureBoard(turn: 8, minions: [(1, 24, 31)]);
        var snapshot = CreateSnapshot(
            CreateState(LobbyPlayer.Create(1), opponent, turn: 10),
            memory);

        var tile = Assert.Single(BattlegroundsOverlayViewStateFactory.Create(snapshot).Opponents);

        Assert.Equal(OpponentPresence.Seen, tile.Presence);
        Assert.Equal(8, tile.LastSeenTurn);
        Assert.Equal(2, tile.TurnsAgo);
        Assert.Equal("Turn 8 · 2 turns ago", tile.LastSeenLine);
        Assert.Equal("Last seen · Turn 8", tile.DetailLastSeenLine);
        Assert.Equal("2T", tile.AgeBadge);
        Assert.Equal("21 HP · 5 armor", tile.HealthLine);
        Assert.Null(tile.BoardPlaceholder);
        var minion = Assert.Single(tile.Board);
        Assert.Equal(1, minion.BoardPosition);
        Assert.Equal("24/31", minion.StatLine);
        Assert.Null(minion.ArtImagePath);
    }

    [Fact]
    public void CreateReportsAnEmptyRecordedBoardSeparatelyFromNeverFought()
    {
        var opponent = LobbyPlayer.Create(2) with { HeroName = "Reno Jackson", Health = 30 };
        var snapshot = CreateSnapshot(
            CreateState(LobbyPlayer.Create(1), opponent, turn: 3),
            CaptureBoard(turn: 3, minions: []));

        var tile = Assert.Single(BattlegroundsOverlayViewStateFactory.Create(snapshot).Opponents);

        Assert.Equal(OpponentPresence.EmptyBoard, tile.Presence);
        Assert.Equal("Board empty · turn 3", tile.LastSeenLine);
        Assert.Equal("Board was empty", tile.BoardPlaceholder);
        Assert.False(tile.IsStale);
    }

    [Fact]
    public void StaleBoardInformationIsFlaggedForTheOverlay()
    {
        var opponent = LobbyPlayer.Create(2) with { HeroName = "Reno Jackson", Health = 30 };
        var snapshot = CreateSnapshot(
            CreateState(LobbyPlayer.Create(1), opponent, turn: 12),
            CaptureBoard(turn: 8, minions: [(1, 3, 4)]));

        var tile = Assert.Single(BattlegroundsOverlayViewStateFactory.Create(snapshot).Opponents);

        Assert.Equal(4, tile.TurnsAgo);
        Assert.True(tile.IsStale);
    }

    [Fact]
    public void CreateResolvesArtOnlyWhenItIsAlreadyAvailableLocally()
    {
        var opponent = LobbyPlayer.Create(2) with { HeroName = "Reno Jackson", Health = 30 };
        var snapshot = CreateSnapshot(
            CreateState(LobbyPlayer.Create(1), opponent, turn: 4),
            CaptureBoard(turn: 4, minions: [(1, 3, 4), (2, 5, 6)]));

        var tile = Assert.Single(BattlegroundsOverlayViewStateFactory
            .Create(
                snapshot,
                cardDatabase: null,
                new StubCardArtSource("MINION_101", @"C:\cache\cached.img"))
            .Opponents);

        Assert.Equal(@"C:\cache\cached.img", tile.Board[0].ArtImagePath);
        Assert.Null(tile.Board[1].ArtImagePath);
    }

    [Fact]
    public void HeroNamesResolveThroughCardMetadataIncludingSkinCardIds()
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var baseHero = LobbyPlayer.Create(2) with { HeroCardId = "TB_BaconShop_HERO_25", Health = 30 };
        var skinHero = LobbyPlayer.Create(3) with { HeroCardId = "BG22_HERO_000_SKIN_E", Health = 30 };
        var database = new IceCrow.Hearthstone.Data.InMemoryCardDatabase();
        database.Replace(new IceCrow.Hearthstone.Data.HearthstoneDataSnapshot(
            new IceCrow.Hearthstone.Data.HearthstoneDataVersion(
                1, "v1", null, new string('0', 64), DateTimeOffset.UnixEpoch),
            [],
            [
                Hero("TB_BaconShop_HERO_25", 100, "Yogg-Saron"),
                Hero("BG22_HERO_000", 101, "Ragnaros"),
            ]));
        var state = new BattlegroundsState(
            IsActive: true,
            Turn: 5,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: 1,
            CurrentOpponentPlayerId: 2,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(baseHero).SetPlayer(skinHero));

        var opponents = BattlegroundsOverlayViewStateFactory
            .Create(CreateSnapshot(state), database)
            .Opponents
            .ToDictionary(tile => tile.PlayerId);

        Assert.Equal("Yogg-Saron", opponents[2].HeroName);
        Assert.Equal("Ragnaros", opponents[3].HeroName);
        Assert.Equal("BG22_HERO_000_SKIN_E", opponents[3].HeroCardId);
    }

    [Fact]
    public void UnresolvedHeroCardIdFallsBackToUnknownHeroNotTheRawId()
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var unresolved = LobbyPlayer.Create(2) with { HeroCardId = "BG99_HERO_FUTURE", Health = 30 };
        var anonymous = LobbyPlayer.Create(3) with { Health = 30 };
        var state = new BattlegroundsState(
            IsActive: true,
            Turn: 5,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: 1,
            CurrentOpponentPlayerId: 2,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(unresolved).SetPlayer(anonymous));

        var opponents = BattlegroundsOverlayViewStateFactory
            .Create(CreateSnapshot(state), cardDatabase: null)
            .Opponents
            .ToDictionary(tile => tile.PlayerId);

        Assert.Equal("Unknown hero", opponents[2].HeroName);
        Assert.Equal("BG99_HERO_FUTURE", opponents[2].HeroCardId);
        Assert.Equal("Player 3", opponents[3].HeroName);
    }

    [Fact]
    public void EntityProvidedHeroNameWinsOverTheUnknownFallback()
    {
        var local = LobbyPlayer.Create(1) with { HeroName = "Local" };
        var named = LobbyPlayer.Create(2) with
        {
            HeroName = "Reno Jackson",
            HeroCardId = "BG99_HERO_FUTURE",
            Health = 30,
        };
        var state = new BattlegroundsState(
            IsActive: true,
            Turn: 5,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: 1,
            CurrentOpponentPlayerId: 2,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(named));

        var tile = Assert.Single(BattlegroundsOverlayViewStateFactory
            .Create(CreateSnapshot(state), cardDatabase: null)
            .Opponents);

        Assert.Equal("Reno Jackson", tile.HeroName);
    }

    private static IceCrow.Hearthstone.Data.BattlegroundsHeroDefinition Hero(
        string cardId,
        int dbfId,
        string name) => new(
            dbfId,
            cardId,
            name,
            name,
            null,
            null,
            null,
            null,
            null,
            IceCrow.Hearthstone.Data.CardImageInfo.Empty);

    [Fact]
    public void ViewStateCopiesCallerOwnedCollections()
    {
        var progression = new List<string> { "T2·3" };
        var board = new List<MinionTileViewState>
        {
            MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null),
        };
        var opponent = CreateOpponentViewState(progression, board);
        var opponents = new List<OpponentOverlayViewState> { opponent };
        var viewState = new BattlegroundsOverlayViewState(true, opponents);

        progression.Add("T3·5");
        board.Clear();
        opponents.Clear();

        Assert.Single(viewState.Opponents);
        Assert.Equal(["T2·3"], opponent.ProgressionRows);
        Assert.Single(opponent.Board);
    }

    [Fact]
    public void EqualViewStatesCompareEqualSoTheOverlayCanSkipWork()
    {
        var first = new BattlegroundsOverlayViewState(true, [CreateOpponentViewState()]);
        var second = new BattlegroundsOverlayViewState(true, [CreateOpponentViewState()]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ChangedTurnAgeMakesTheViewStateDifferent()
    {
        Assert.NotEqual(
            CreateOpponentViewState(turnsAgo: 1),
            CreateOpponentViewState(turnsAgo: 2));
    }

    private static OpponentOverlayViewState CreateOpponentViewState(
        IEnumerable<string>? progressionRows = null,
        IEnumerable<MinionTileViewState>? board = null,
        int turnsAgo = 1) => new(
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
            progressionRows: progressionRows ?? ["T2·3"],
            board: board ?? [MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null)]);

    private static BattlegroundsState CreateState(
        LobbyPlayer local,
        LobbyPlayer opponent,
        int turn = 8) => new(
            IsActive: true,
            Turn: turn,
            Phase: BattlegroundsPhase.Recruit,
            LocalPlayerId: local.PlayerId,
            CurrentOpponentPlayerId: opponent.PlayerId,
            Lobby: LobbyState.Empty.SetPlayer(local).SetPlayer(opponent));

    private static OpponentMemory CaptureBoard(
        int turn,
        IReadOnlyList<(int ZonePosition, int Attack, int Health)> minions)
    {
        var store = new EntityStore();
        foreach (var (zonePosition, attack, health) in minions)
        {
            var entityId = 100 + zonePosition;
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
            _ = store.Apply(new EntityRevealed(
                Timestamp,
                BlockId: null,
                EntityId: entityId,
                EntityName: $"Minion {entityId}",
                CardId: $"MINION_{entityId}"));
        }

        var service = new OpponentMemoryService();
        _ = service.Capture(2, turn, store.CreateAllSnapshots(), Timestamp);
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

    private static TrackingSnapshot CreateSnapshot(
        BattlegroundsState state,
        OpponentMemory? opponentMemory = null) => new(
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
            OpponentMemory: opponentMemory ?? OpponentMemory.Empty,
            LobbyTimeline: LobbyTimelineSnapshot.Empty);

    private sealed class StubCardArtSource(string availableCardId, string availableArtPath)
        : ICardArtSource
    {
        public bool TryGetArtPath(string cardId, out string artPath)
        {
            var available = string.Equals(cardId, availableCardId, StringComparison.Ordinal);
            artPath = available ? availableArtPath : string.Empty;
            return available;
        }
    }
}
