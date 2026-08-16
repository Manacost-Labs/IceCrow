namespace IceCrow.Battlegrounds.Memory.Tests;

public sealed class LobbyTimelineTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void DuplicatePlayerTechLevelProducesOneUpgrade()
    {
        var timeline = StartTimeline(Player(tavernTier: 1));

        timeline.Update(State(Player(tavernTier: 2), turn: 3), Timestamp.AddMinutes(1));
        timeline.Update(State(Player(tavernTier: 2), turn: 3), Timestamp.AddMinutes(2));

        var upgrade = Assert.IsType<TavernUpgraded>(
            Assert.Single(timeline.GetPlayer(2)!.Events));
        Assert.Equal(2, upgrade.TavernTier);
        Assert.Equal(3, upgrade.Turn);
    }

    [Fact]
    public void MultipleTriplesUseCumulativeValuesInsteadOfAssumingPlusOne()
    {
        var timeline = StartTimeline(Player(triples: 0));

        timeline.Update(State(Player(triples: 2), turn: 4), Timestamp.AddMinutes(1));
        timeline.Update(State(Player(triples: 2), turn: 4), Timestamp.AddMinutes(2));
        timeline.Update(State(Player(triples: 5), turn: 6), Timestamp.AddMinutes(3));

        var triples = timeline.GetPlayer(2)!.Events.OfType<TripleCreated>().ToArray();
        Assert.Equal(
            [
                new TripleCreated(2, 4, Timestamp.AddMinutes(1), 0, 2),
                new TripleCreated(2, 6, Timestamp.AddMinutes(3), 2, 5),
            ],
            triples);
        Assert.Equal([2, 3], triples.Select(static triple => triple.Amount));
    }

    [Fact]
    public void ReconnectLikeTierReplayDoesNotDuplicatePriorUpgrades()
    {
        var timeline = StartTimeline(Player(tavernTier: 1));

        timeline.Update(State(Player(tavernTier: 2), turn: 3), Timestamp.AddMinutes(1));
        timeline.Update(State(Player(tavernTier: 1), turn: 5), Timestamp.AddMinutes(2));
        timeline.Update(State(Player(tavernTier: 2), turn: 5), Timestamp.AddMinutes(3));
        timeline.Update(State(Player(tavernTier: 3), turn: 5), Timestamp.AddMinutes(4));

        var upgrades = timeline.GetPlayer(2)!.Events.OfType<TavernUpgraded>().ToArray();
        Assert.Equal([2, 3], upgrades.Select(static upgrade => upgrade.TavernTier));
        Assert.Equal([3, 5], upgrades.Select(static upgrade => upgrade.Turn));
    }

    [Fact]
    public void ArmorTransitionDoesNotInventExactDamage()
    {
        var timeline = StartTimeline(Player(health: 40, armor: 5));

        timeline.Update(
            State(Player(health: 35, armor: 0), turn: 2),
            Timestamp.AddMinutes(1));
        timeline.Update(
            State(Player(health: 30, armor: 0), turn: 3),
            Timestamp.AddMinutes(2));

        var damage = timeline.GetPlayer(2)!.Events.OfType<DamageTaken>().ToArray();
        Assert.Null(damage[0].ExactDamage);
        Assert.Equal((40, 35, 5, 0), (
            damage[0].PreviousHealth,
            damage[0].Health,
            damage[0].PreviousArmor,
            damage[0].Armor));
        Assert.Equal(5, damage[1].ExactDamage);
    }

    [Fact]
    public void ExtremeHealthTransitionDoesNotOverflowExactDamage()
    {
        var timeline = StartTimeline(Player(health: int.MaxValue));

        timeline.Update(
            State(Player(health: int.MinValue), turn: 2),
            Timestamp.AddMinutes(1));

        var damage = Assert.Single(timeline.GetPlayer(2)!.Events.OfType<DamageTaken>());
        Assert.Null(damage.ExactDamage);
        Assert.Equal(int.MaxValue, damage.PreviousHealth);
        Assert.Equal(int.MinValue, damage.Health);
    }

    [Fact]
    public void PlayerDeathIsRecordedOnce()
    {
        var timeline = StartTimeline(Player(health: 10, isAlive: true));
        var deadPlayer = Player(health: 0, isAlive: false);

        timeline.Update(State(deadPlayer, turn: 8), Timestamp.AddMinutes(1));
        timeline.Update(State(deadPlayer, turn: 8), Timestamp.AddMinutes(2));

        var death = Assert.Single(timeline.GetPlayer(2)!.Events.OfType<PlayerDied>());
        Assert.Equal(new PlayerDied(2, 8, Timestamp.AddMinutes(1), 0, 0), death);
    }

    [Fact]
    public void EventsAreSortedByTurnEvenWhenUpdatesArriveOutOfOrder()
    {
        var timeline = StartTimeline(Player(tavernTier: 1));

        timeline.Update(State(Player(tavernTier: 2), turn: 5), Timestamp.AddMinutes(2));
        timeline.Update(State(Player(tavernTier: 3), turn: 4), Timestamp.AddMinutes(1));

        Assert.Equal(
            [4, 5],
            timeline.GetPlayer(2)!.Events.Select(static timelineEvent => timelineEvent.Turn));
        Assert.Equal([4, 5], timeline.Events.Select(static timelineEvent => timelineEvent.Turn));
    }

    [Fact]
    public void ObservedBoardProducesOpponentObservedEvent()
    {
        var timeline = StartTimeline(Player());
        var board = BoardSnapshot.Capture(
            playerId: 2,
            turn: 3,
            timestamp: Timestamp.AddMinutes(1),
            entities: []);

        timeline.Update(
            State(Player(), turn: 3, phase: BattlegroundsPhase.Combat),
            Timestamp.AddMinutes(1),
            board);

        var observed = Assert.Single(timeline.GetPlayer(2)!.Events.OfType<OpponentObserved>());
        Assert.Equal(new OpponentObserved(2, 3, Timestamp.AddMinutes(1), 0), observed);
    }

    [Fact]
    public void NewMatchClearsTimeline()
    {
        var timeline = StartTimeline(Player(tavernTier: 1));
        timeline.Update(State(Player(tavernTier: 2), turn: 3), Timestamp.AddMinutes(1));
        Assert.NotEmpty(timeline.Events);

        timeline.Update(
            BattlegroundsState.Empty with { Phase = BattlegroundsPhase.GameOver },
            Timestamp.AddMinutes(2));
        timeline.Update(
            State(Player(tavernTier: 1), turn: 0, phase: BattlegroundsPhase.HeroSelection),
            Timestamp.AddMinutes(3));

        Assert.Empty(timeline.Events);
        Assert.Equal(1, timeline.GetPlayer(2)!.TavernTier);

        timeline.Reset();
        Assert.Empty(timeline.Players);
    }

    [Fact]
    public void PerPlayerTimelineRetainsOnlyTheNewestConfiguredEvents()
    {
        var timeline = new LobbyTimeline(maximumPlayers: 2, maximumEventsPerPlayer: 3);
        timeline.Update(State(Player(tavernTier: 1), turn: 1), Timestamp);

        for (var tier = 2; tier <= 6; tier++)
        {
            timeline.Update(
                State(Player(tavernTier: tier), turn: tier),
                Timestamp.AddMinutes(tier));
        }

        Assert.Equal(
            [4, 5, 6],
            timeline.GetPlayer(2)!.Events
                .OfType<TavernUpgraded>()
                .Select(static upgrade => upgrade.TavernTier));
        Assert.Equal(3, timeline.EventCount);
        Assert.Equal(3, timeline.MaximumPlayerEventCount);
    }

    [Fact]
    public void MutationWorkCountsInsertsAndEvictionsBeyondRetainedCount()
    {
        var timeline = new LobbyTimeline(maximumPlayers: 4, maximumEventsPerPlayer: 2);
        timeline.Update(State(Player(tavernTier: 1), turn: 1), Timestamp);
        Assert.Equal(0, timeline.MutationWorkUnits);

        // Two inserts fill the bounded history: one work unit each.
        timeline.Update(State(Player(tavernTier: 2), turn: 2), Timestamp.AddMinutes(1));
        timeline.Update(State(Player(tavernTier: 3), turn: 3), Timestamp.AddMinutes(2));
        Assert.Equal(2, timeline.MutationWorkUnits);
        Assert.Equal(2, timeline.EventCount);

        // A saturated history evicts on every insert: retained count stays
        // constant while the mutation work keeps growing.
        timeline.Update(State(Player(tavernTier: 4), turn: 4), Timestamp.AddMinutes(3));
        Assert.Equal(4, timeline.MutationWorkUnits);
        Assert.Equal(2, timeline.EventCount);

        // An observation that adds nothing charges nothing.
        timeline.Update(State(Player(tavernTier: 4), turn: 5), Timestamp.AddMinutes(4));
        Assert.Equal(4, timeline.MutationWorkUnits);

        timeline.Reset();
        Assert.Equal(0, timeline.MutationWorkUnits);
    }

    private static LobbyTimeline StartTimeline(LobbyPlayer player)
    {
        var timeline = new LobbyTimeline();
        timeline.Update(State(player, turn: 1), Timestamp);
        return timeline;
    }

    private static BattlegroundsState State(
        LobbyPlayer player,
        int turn,
        BattlegroundsPhase phase = BattlegroundsPhase.Recruit) => new(
            IsActive: true,
            Turn: turn,
            Phase: phase,
            LocalPlayerId: 1,
            CurrentOpponentPlayerId: player.PlayerId,
            Lobby: LobbyState.Empty.SetPlayer(player));

    private static LobbyPlayer Player(
        int tavernTier = 1,
        int triples = 0,
        int health = 40,
        int armor = 0,
        bool isAlive = true) => new(
            PlayerId: 2,
            HeroEntityId: 200,
            HeroName: "Reno",
            HeroCardId: "TB_BaconShop_HERO_41",
            Health: health,
            Armor: armor,
            TavernTier: tavernTier,
            Triples: triples,
            IsAlive: isAlive);
}
