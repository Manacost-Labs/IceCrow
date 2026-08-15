using System.Globalization;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Battlegrounds.Memory.Tests;

public sealed class OpponentBoardDiffCalculatorTests
{
    private const int OpponentPlayerId = 2;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void IdenticalBoardsProduceNoChange()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_BRANN", 1, 4, 6), (102, "BG_TITUS", 2, 5, 5));
        var current = CreateBoard(turn: 8, (101, "BG_BRANN", 1, 4, 6), (102, "BG_TITUS", 2, 5, 5));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.False(changes.HasChanges);
        Assert.Equal(BoardChangeSignificance.NoChange, changes.Significance);
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.RemovedMinions);
        Assert.Empty(changes.ChangedMinions);
    }

    [Fact]
    public void NewMinionOnTheLatestBoardIsAdded()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_BRANN", 1, 4, 6));
        var current = CreateBoard(
            turn: 10,
            (101, "BG_BRANN", 1, 4, 6),
            (105, "BG_MALCHEZAAR", 2, 9, 9));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var added = Assert.Single(changes.AddedMinions);
        Assert.Equal("BG_MALCHEZAAR", added.CardId);
        Assert.Empty(changes.RemovedMinions);
        Assert.Empty(changes.ChangedMinions);
        Assert.Equal(BoardChangeSignificance.Minor, changes.Significance);
    }

    [Fact]
    public void MissingMinionOnTheLatestBoardIsRemoved()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_BRANN", 1, 4, 6),
            (102, "BG_IMP", 2, 1, 1));
        var current = CreateBoard(turn: 10, (101, "BG_BRANN", 1, 4, 6));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var removed = Assert.Single(changes.RemovedMinions);
        Assert.Equal("BG_IMP", removed.CardId);
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.ChangedMinions);
    }

    [Fact]
    public void StatGrowthOnTheSameEntityRecordsExactDeltas()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_BRANN", 1, 12, 18));
        var current = CreateBoard(turn: 10, (101, "BG_BRANN", 1, 31, 42));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal(MinionIdentity.SameEntity, change.Identity);
        Assert.Equal(19, change.AttackDelta);
        Assert.Equal(24, change.HealthDelta);
        Assert.True(change.HasStatChange);
        Assert.Equal(43, changes.StatGrowth);
        Assert.Equal(BoardChangeSignificance.Major, changes.Significance);
    }

    [Fact]
    public void StatDecreaseIsRecordedAsObservedWithoutInterpretation()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_BRANN", 1, 10, 10));
        var current = CreateBoard(turn: 8, (101, "BG_BRANN", 1, 6, 7));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal(-4, change.AttackDelta);
        Assert.Equal(-3, change.HealthDelta);
        Assert.Equal(0, changes.StatGrowth);
        Assert.Equal(BoardChangeSignificance.Minor, changes.Significance);
    }

    [Fact]
    public void RecreatedEntitiesAreMatchedByCardId()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_BRANN", 1, 4, 6));
        var current = CreateBoard(turn: 10, (301, "BG_BRANN", 1, 8, 9));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal(MinionIdentity.LikelySameCard, change.Identity);
        Assert.Equal(4, change.AttackDelta);
        Assert.Equal(3, change.HealthDelta);
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.RemovedMinions);
    }

    [Fact]
    public void MixedRosterAndStatChangesAreAllRecorded()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_BRANN", 1, 18, 22),
            (102, "BG_TITUS", 2, 5, 5),
            (103, "BG_IMP", 3, 1, 1));
        var current = CreateBoard(
            turn: 10,
            (201, "BG_BRANN", 1, 41, 49),
            (202, "BG_TITUS", 2, 5, 5),
            (205, "BG_MALCHEZAAR", 3, 9, 9));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Equal("BG_MALCHEZAAR", Assert.Single(changes.AddedMinions).CardId);
        Assert.Equal("BG_IMP", Assert.Single(changes.RemovedMinions).CardId);
        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal("BG_BRANN", change.Current.CardId);
        Assert.Equal(23, change.AttackDelta);
        Assert.Equal(27, change.HealthDelta);
        Assert.Equal(BoardChangeSignificance.Major, changes.Significance);
    }

    [Fact]
    public void EmptyBoardToPopulatedBoardIsAllAdded()
    {
        var previous = CreateBoard(turn: 3);
        var current = CreateBoard(
            turn: 6,
            (101, "BG_BRANN", 1, 4, 6),
            (102, "BG_TITUS", 2, 5, 5));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Equal(2, changes.AddedMinions.Count);
        Assert.Empty(changes.RemovedMinions);
        Assert.Equal(BoardChangeSignificance.Major, changes.Significance);
    }

    [Fact]
    public void PopulatedBoardToEmptyBoardIsAllRemoved()
    {
        var previous = CreateBoard(
            turn: 6,
            (101, "BG_BRANN", 1, 4, 6),
            (102, "BG_TITUS", 2, 5, 5));
        var current = CreateBoard(turn: 9);

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Empty(changes.AddedMinions);
        Assert.Equal(2, changes.RemovedMinions.Count);
        Assert.Equal(BoardChangeSignificance.Major, changes.Significance);
    }

    [Fact]
    public void PositionChangeAloneIsRecordedWithoutStatChange()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_TITUS", 5, 5, 5));
        var current = CreateBoard(turn: 8, (101, "BG_TITUS", 7, 5, 5));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.False(change.HasStatChange);
        Assert.True(change.HasPositionChange);
        Assert.Equal(2, change.PositionDelta);
        Assert.Equal(BoardChangeSignificance.Minor, changes.Significance);
    }

    [Fact]
    public void ObservationsManyTurnsApartKeepBothTurnNumbers()
    {
        var previous = CreateBoard(turn: 3, (101, "BG_BRANN", 1, 2, 2));
        var current = CreateBoard(turn: 14, (301, "BG_BRANN", 1, 30, 30));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Equal(3, changes.PreviousTurn);
        Assert.Equal(14, changes.CurrentTurn);
    }

    [Fact]
    public void DuplicateCardIdsArePairedDeterministicallyInBoardOrder()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 2, 2),
            (102, "BG_RAT", 2, 5, 5));
        var current = CreateBoard(turn: 9, (301, "BG_RAT", 1, 2, 2));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var removed = Assert.Single(changes.RemovedMinions);
        Assert.Equal(5, removed.Attack);
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.ChangedMinions);
    }

    [Fact]
    public void DuplicateCopiesWithDifferentStatsAreMarkedAmbiguous()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 2, 2),
            (102, "BG_RAT", 2, 40, 40));
        var current = CreateBoard(
            turn: 10,
            (301, "BG_RAT", 1, 45, 45),
            (302, "BG_RAT", 2, 3, 3));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Equal(2, changes.ChangedMinions.Count);
        Assert.All(
            changes.ChangedMinions,
            change => Assert.Equal(MinionIdentity.AmbiguousCardCopy, change.Identity));
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.RemovedMinions);
        Assert.Equal(0, changes.StatGrowth);
        Assert.Equal(BoardChangeSignificance.Minor, changes.Significance);
    }

    [Fact]
    public void SwappedDuplicateStatsProduceNoPhantomStatTransition()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 2, 2),
            (102, "BG_RAT", 2, 40, 40));
        var current = CreateBoard(
            turn: 9,
            (301, "BG_RAT", 1, 40, 40),
            (302, "BG_RAT", 2, 2, 2));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        // Stat-identical copies pair first, so no pair claims 2/2 → 40/40.
        Assert.DoesNotContain(changes.ChangedMinions, change => change.HasStatChange);
        Assert.Empty(changes.AddedMinions);
        Assert.Empty(changes.RemovedMinions);
    }

    [Fact]
    public void DuplicateGroupPairsIdenticalStatsBeforeArbitraryLeftovers()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 2, 2),
            (102, "BG_RAT", 2, 40, 40));
        var current = CreateBoard(
            turn: 9,
            (301, "BG_RAT", 1, 40, 40),
            (302, "BG_RAT", 2, 3, 3));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions, static change => change.HasStatChange);
        Assert.Equal(MinionIdentity.AmbiguousCardCopy, change.Identity);
        Assert.Equal(2, change.Previous.Attack);
        Assert.Equal(3, change.Current.Attack);
    }

    [Fact]
    public void ThreeDuplicateCopiesKeepRosterArithmeticExact()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 1, 1),
            (102, "BG_RAT", 2, 2, 2),
            (103, "BG_RAT", 3, 3, 3));
        var current = CreateBoard(
            turn: 10,
            (301, "BG_RAT", 1, 2, 2),
            (302, "BG_RAT", 2, 9, 9));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Empty(changes.AddedMinions);
        var removed = Assert.Single(changes.RemovedMinions);
        Assert.Equal("BG_RAT", removed.CardId);
        var change = Assert.Single(changes.ChangedMinions, static change => change.HasStatChange);
        Assert.Equal(MinionIdentity.AmbiguousCardCopy, change.Identity);
        Assert.Equal(9, change.Current.Attack);
    }

    [Fact]
    public void UniqueLeftoverCopyAfterEntityMatchStaysLikely()
    {
        var previous = CreateBoard(
            turn: 7,
            (101, "BG_RAT", 1, 2, 2),
            (102, "BG_RAT", 2, 5, 5));
        var current = CreateBoard(
            turn: 9,
            (101, "BG_RAT", 1, 2, 2),
            (301, "BG_RAT", 2, 8, 8));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal(MinionIdentity.LikelySameCard, change.Identity);
        Assert.Equal(3, change.AttackDelta);
        Assert.Equal(6, changes.StatGrowth);
    }

    [Fact]
    public void ReusedEntityIdWithDifferentCardIsATransformNotAMatch()
    {
        var previous = CreateBoard(turn: 7, (101, "BG_RAT", 1, 2, 2));
        var current = CreateBoard(turn: 8, (101, "BG_WOLF", 1, 6, 6));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Equal("BG_WOLF", Assert.Single(changes.AddedMinions).CardId);
        Assert.Equal("BG_RAT", Assert.Single(changes.RemovedMinions).CardId);
        Assert.Empty(changes.ChangedMinions);
    }

    [Fact]
    public void ComparingABoardWithItselfReportsNoChanges()
    {
        var board = CreateBoard(
            turn: 7,
            (101, "BG_BRANN", 1, 4, 6),
            (102, "BG_RAT", 2, 2, 2),
            (103, "BG_RAT", 3, 2, 2));

        var changes = OpponentBoardDiffCalculator.Compare(board, board);

        Assert.False(changes.HasChanges);
        Assert.Equal(BoardChangeSignificance.NoChange, changes.Significance);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 7)]
    [InlineData(7, 0)]
    [InlineData(7, 7)]
    [InlineData(3, 7)]
    [InlineData(7, 4)]
    public void RosterArithmeticStaysConsistentAcrossBoardSizes(
        int previousCount,
        int currentCount)
    {
        // Duplicate-heavy boards: every second minion shares a card id.
        var previous = CreateBoard(
            turn: 6,
            Enumerable.Range(1, previousCount)
                .Select(index => (100 + index, (string?)$"BG_CARD_{index % 2}", index, index, index + 1))
                .ToArray());
        var current = CreateBoard(
            turn: 9,
            Enumerable.Range(1, currentCount)
                .Select(index => (300 + index, (string?)$"BG_CARD_{index % 2}", index, index * 2, index + 3))
                .ToArray());

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var matchedFromPrevious = previousCount - changes.RemovedMinions.Count;
        var matchedFromCurrent = currentCount - changes.AddedMinions.Count;
        Assert.Equal(matchedFromPrevious, matchedFromCurrent);
        Assert.True(matchedFromPrevious >= 0);
        Assert.True(changes.ChangedMinions.Count <= matchedFromPrevious);
        Assert.Equal(
            changes.ChangedMinions.Count,
            changes.ChangedMinions.DistinctBy(static change => change.Current.EntityId).Count());
        Assert.Equal(
            changes.ChangedMinions.Count,
            changes.ChangedMinions.DistinctBy(static change => change.Previous.EntityId).Count());
    }

    [Fact]
    public void MinionsWithoutCardIdAreNeverMatchedAcrossEntities()
    {
        var previous = CreateBoard(turn: 7, (101, null, 1, 3, 3));
        var current = CreateBoard(turn: 9, (301, null, 1, 8, 8));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        Assert.Single(changes.AddedMinions);
        Assert.Single(changes.RemovedMinions);
        Assert.Empty(changes.ChangedMinions);
    }

    [Fact]
    public void UnknownCardIdStillMatchesWhenTheEntityIsTheSame()
    {
        var previous = CreateBoard(turn: 7, (101, null, 1, 3, 3));
        var current = CreateBoard(turn: 8, (101, null, 1, 5, 6));

        var changes = OpponentBoardDiffCalculator.Compare(previous, current);

        var change = Assert.Single(changes.ChangedMinions);
        Assert.Equal(MinionIdentity.SameEntity, change.Identity);
        Assert.Equal(2, change.AttackDelta);
    }

    [Fact]
    public void BoardsOfDifferentOpponentsCannotBeCompared()
    {
        var previous = CreateBoard(turn: 7, playerId: 2);
        var current = CreateBoard(turn: 8, playerId: 3);

        Assert.Throws<ArgumentException>(
            () => OpponentBoardDiffCalculator.Compare(previous, current));
    }

    [Fact]
    public void HistoryExposesTheFightBeforeLatestForComparison()
    {
        var boardA = CreateBoard(turn: 5, (101, "BG_BRANN", 1, 2, 2));
        var boardB = CreateBoard(turn: 8, (201, "BG_BRANN", 1, 10, 10));
        var boardC = CreateBoard(turn: 11, (301, "BG_BRANN", 1, 25, 25));

        var history = OpponentBoardHistory
            .Start(boardA)
            .Add(boardB, maximumSnapshots: 8)
            .Add(boardC, maximumSnapshots: 8);

        Assert.Same(boardC, history.Latest);
        Assert.Same(boardB, history.Previous);
        var changes = OpponentBoardDiffCalculator.Compare(history.Previous!, history.Latest);
        Assert.Equal(8, changes.PreviousTurn);
        Assert.Equal(11, changes.CurrentTurn);
        Assert.Equal(15, Assert.Single(changes.ChangedMinions).AttackDelta);
    }

    [Fact]
    public void SingleObservationHasNoPreviousBoard()
    {
        var history = OpponentBoardHistory.Start(CreateBoard(turn: 5));

        Assert.Null(history.Previous);
    }

    private static BoardSnapshot CreateBoard(
        int turn,
        params (int EntityId, string? CardId, int ZonePosition, int Attack, int Health)[] minions) =>
        CreateBoard(turn, OpponentPlayerId, minions);

    private static BoardSnapshot CreateBoard(
        int turn,
        int playerId,
        params (int EntityId, string? CardId, int ZonePosition, int Attack, int Health)[] minions)
    {
        // Each board uses a fresh store because Battlegrounds recreates opponent
        // warband entities for every combat.
        var store = new EntityStore();
        foreach (var (entityId, cardId, zonePosition, attack, health) in minions)
        {
            SetTag(store, entityId, "CARDTYPE", "MINION");
            SetTag(store, entityId, "ZONE", "PLAY");
            SetTag(store, entityId, "CONTROLLER", playerId.ToString(CultureInfo.InvariantCulture));
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

        return BoardSnapshot.Capture(playerId, turn, Timestamp, store.CreateAllSnapshots());
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
