using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Battlegrounds.Tests;

public sealed class BattlegroundsReducerTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        18,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void TracksHeroSelectionRecruitCombatAndGameOverSequence()
    {
        var fixture = new NormalizedEventFixture();
        var state = BattlegroundsState.Empty;

        state = Apply(state, new BattlegroundsGameStarted(Timestamp));
        Assert.True(state.IsActive);
        Assert.Equal(BattlegroundsPhase.HeroSelection, state.Phase);

        state = ApplyAll(state, fixture.CreateLobbyEvents());
        Assert.Equal(BattlegroundsPhase.HeroSelection, state.Phase);

        state = Apply(state, fixture.Tag(500, "TURN", "1"));
        Assert.Equal(1, state.Turn);
        Assert.Equal(BattlegroundsPhase.Recruit, state.Phase);

        state = Apply(state, fixture.Tag(500, "2022", "1"));
        Assert.Equal(BattlegroundsPhase.Recruit, state.Phase);
        state = Apply(state, fixture.Tag(500, "2022", "0"));
        Assert.Equal(BattlegroundsPhase.Combat, state.Phase);

        state = Apply(state, fixture.Tag(500, "TURN", "3"));
        Assert.Equal(2, state.Turn);
        Assert.Equal(BattlegroundsPhase.Recruit, state.Phase);

        state = Apply(state, fixture.Tag(500, "3533", "1"));
        Assert.Equal(BattlegroundsPhase.Recruit, state.Phase);
        state = Apply(state, fixture.Tag(500, "3533", "0"));
        Assert.Equal(BattlegroundsPhase.Combat, state.Phase);

        state = Apply(state, fixture.Tag(1, "PLAYSTATE", "5"));
        Assert.False(state.IsActive);
        Assert.Equal(BattlegroundsPhase.GameOver, state.Phase);
        Assert.Null(state.CurrentOpponentPlayerId);
        Assert.False(state.Lobby.GetPlayer(1)?.IsAlive);
    }

    [Fact]
    public void TracksLobbyPlayerAndOpponentFromNormalizedEntityChanges()
    {
        var fixture = new NormalizedEventFixture();
        var state = Apply(
            BattlegroundsState.Empty,
            new BattlegroundsGameStarted(Timestamp));

        state = ApplyAll(state, fixture.CreateLobbyEvents());

        Assert.Equal(2, state.Lobby.Count);
        Assert.Equal(1, state.LocalPlayerId);
        Assert.Equal(2, state.CurrentOpponentPlayerId);

        var localPlayer = Assert.IsType<LobbyPlayer>(state.Lobby.GetPlayer(1));
        Assert.Equal(101, localPlayer.HeroEntityId);
        Assert.Equal("Friendly Hero", localPlayer.HeroName);
        Assert.Equal("TB_BaconShop_HERO_01", localPlayer.HeroCardId);
        Assert.Equal(35, localPlayer.Health);
        Assert.Equal(10, localPlayer.Armor);
        Assert.Equal(2, localPlayer.TavernTier);
        Assert.Equal(1, localPlayer.Triples);
        Assert.True(localPlayer.IsAlive);
    }

    [Fact]
    public void RecordedNormalizedFixtureIsDeterministic()
    {
        var events = CreateRecordedFixture();

        var first = Replay(events);
        var second = Replay(events);

        Assert.Equal(first, second);
        Assert.Equal(
            first.Lobby.Players,
            second.Lobby.Players);
        Assert.Equal(BattlegroundsPhase.GameOver, first.Phase);
        Assert.Equal(2, first.Turn);
    }

    [Fact]
    public void SnapshotAndLobbyRemainImmutableAfterLaterChanges()
    {
        var fixture = new NormalizedEventFixture();
        var state = Apply(
            BattlegroundsState.Empty,
            new BattlegroundsGameStarted(Timestamp, LocalPlayerId: 1));
        state = ApplyAll(state, fixture.CreateLobbyEvents());
        var originalLobby = state.Lobby;
        var originalPlayer = Assert.IsType<LobbyPlayer>(originalLobby.GetPlayer(1));

        state = Apply(state, fixture.Tag(101, "DAMAGE", "12"));

        Assert.Equal(35, originalPlayer.Health);
        Assert.Equal(28, state.Lobby.GetPlayer(1)?.Health);
        Assert.NotSame(originalLobby, state.Lobby);
    }

    [Fact]
    public void IgnoresEntityChangesBeforeBattlegroundsGameStarts()
    {
        var fixture = new NormalizedEventFixture();

        var state = Apply(
            BattlegroundsState.Empty,
            fixture.Tag(1, "PLAYER_ID", "1"));

        Assert.Equal(BattlegroundsState.Empty, state);
    }

    [Fact]
    public void GameStartResetsPreviousLobbyAndTurn()
    {
        var fixture = new NormalizedEventFixture();
        var state = Apply(
            BattlegroundsState.Empty,
            new BattlegroundsGameStarted(Timestamp, LocalPlayerId: 1));
        state = ApplyAll(state, fixture.CreateLobbyEvents());
        state = Apply(state, fixture.Tag(500, "TURN", "5"));

        state = Apply(
            state,
            new BattlegroundsGameStarted(Timestamp.AddMinutes(20), LocalPlayerId: 8));

        Assert.True(state.IsActive);
        Assert.Equal(0, state.Turn);
        Assert.Equal(BattlegroundsPhase.HeroSelection, state.Phase);
        Assert.Equal(8, state.LocalPlayerId);
        Assert.Equal(0, state.Lobby.Count);
    }

    private static BattlegroundsState Replay(IReadOnlyList<BattlegroundsEvent> events) =>
        ApplyAll(BattlegroundsState.Empty, events);

    private static List<BattlegroundsEvent> CreateRecordedFixture()
    {
        var fixture = new NormalizedEventFixture();
        var events = new List<BattlegroundsEvent>
        {
            new BattlegroundsGameStarted(Timestamp),
        };
        events.AddRange(fixture.CreateLobbyEvents());
        events.Add(fixture.Tag(500, "TURN", "1"));
        events.Add(fixture.Tag(500, "2022", "1"));
        events.Add(fixture.Tag(500, "2022", "0"));
        events.Add(fixture.Tag(500, "TURN", "3"));
        events.Add(fixture.Tag(500, "3533", "1"));
        events.Add(fixture.Tag(500, "3533", "0"));
        events.Add(fixture.Tag(1, "PLAYSTATE", "5"));
        return events;
    }

    private static BattlegroundsState ApplyAll(
        BattlegroundsState state,
        IEnumerable<BattlegroundsEvent> events)
    {
        foreach (var gameEvent in events)
        {
            state = Apply(state, gameEvent);
        }

        return state;
    }

    private static BattlegroundsState Apply(
        BattlegroundsState state,
        BattlegroundsEvent gameEvent) =>
        BattlegroundsReducer.Apply(state, gameEvent);

    private sealed class NormalizedEventFixture
    {
        private readonly EntityStore _store = new();

        public List<BattlegroundsEvent> CreateLobbyEvents()
        {
            var events = new List<BattlegroundsEvent>
            {
                Tag(1, "CARDTYPE", "PLAYER"),
                Tag(1, "PLAYER_ID", "1"),
                Tag(1, "CURRENT_PLAYER", "1"),
                Tag(1, "HERO_ENTITY", "101"),
                Tag(1, "NEXT_OPPONENT_PLAYER_ID", "2"),
                Tag(1, "PLAYSTATE", "1"),
                Tag(2, "CARDTYPE", "PLAYER"),
                Tag(2, "PLAYER_ID", "2"),
                Tag(2, "HERO_ENTITY", "102"),
                Tag(2, "PLAYSTATE", "1"),
                Tag(101, "CARDTYPE", "HERO"),
                Tag(101, "PLAYER_ID", "1"),
                Tag(101, "HEALTH", "40"),
                Tag(101, "DAMAGE", "5"),
                Tag(101, "ARMOR", "10"),
                Tag(101, "PLAYER_TECH_LEVEL", "2"),
                Tag(101, "PLAYER_TRIPLES", "1"),
                ObserveIdentity(101, "Friendly Hero", "TB_BaconShop_HERO_01"),
                Tag(102, "CARDTYPE", "HERO"),
                Tag(102, "PLAYER_ID", "2"),
                Tag(102, "HEALTH", "40"),
                ObserveIdentity(102, "Opponent Hero", "TB_BaconShop_HERO_02"),
            };
            return events;
        }

        public BattlegroundsEntityChanged Tag(
            int entityId,
            string tag,
            string value)
        {
            var mutation = _store.Apply(
                new RawTagChanged(
                    Timestamp,
                    BlockId: null,
                    EntityId: entityId,
                    EntityName: null,
                    Tag: tag,
                    Value: value,
                    IsCreationTag: false));
            return new BattlegroundsEntityChanged(
                Timestamp,
                _store.CreateSnapshot(entityId),
                Assert.IsType<EntityMutation>(mutation));
        }

        private BattlegroundsEntityObserved ObserveIdentity(
            int entityId,
            string entityName,
            string cardId)
        {
            _ = _store.Apply(
                new EntityRevealed(
                    Timestamp,
                    BlockId: null,
                    EntityId: entityId,
                    EntityName: entityName,
                    CardId: cardId));
            return new BattlegroundsEntityObserved(
                Timestamp,
                _store.CreateSnapshot(entityId));
        }
    }
}
