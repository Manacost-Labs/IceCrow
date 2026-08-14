using System.Diagnostics;
using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities.Tests;

public sealed class EntityStoreTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        15,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CreatesEntityFromDeclaration()
    {
        var store = new EntityStore();

        var mutation = store.Apply(new GameEntityDeclared(Timestamp, null, 1));

        Assert.Null(mutation);
        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.Get(1).Id);
    }

    [Fact]
    public void CreatesUnknownEntityWhenFirstTagArrives()
    {
        var store = new EntityStore();

        var mutation = ApplyTag(store, 77, "ATK", "4");

        Assert.Equal(new EntityMutation(77, GameTag.Attack, 0, 4), mutation);
        Assert.Equal(4, store.Get(77).Attack);
    }

    [Fact]
    public void ReturnsPreviousAndNewValuesForTagUpdate()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 10, "ATK", "3");

        var mutation = ApplyTag(store, 10, "ATK", "8");

        Assert.Equal(new EntityMutation(10, GameTag.Attack, 3, 8), mutation);
        Assert.Equal(8, store.Get(10).Attack);
    }

    [Fact]
    public void DuplicateTagDoesNotProduceMutation()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 10, "ATK", "8");

        var duplicate = ApplyTag(store, 10, "ATK", "8");

        Assert.Null(duplicate);
        Assert.Single(store.Get(10).Tags);
    }

    [Fact]
    public void TracksMaximumTagsPerEntityWithoutScanningTheStore()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 1, "ATK", "3");
        _ = ApplyTag(store, 2, "ATK", "4");
        _ = ApplyTag(store, 2, "HEALTH", "10");

        Assert.Equal(2, store.MaximumTagCount);

        _ = ApplyTag(store, 2, "HEALTH", "9");
        Assert.Equal(2, store.MaximumTagCount);
    }

    [Fact]
    public void ExposesControllerAndControllerQuery()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 10, "CONTROLLER", "1");
        _ = ApplyTag(store, 11, "CONTROLLER", "2");

        var controlled = store.GetEntitiesByController(1).ToArray();

        Assert.Equal(1, store.Get(10).Controller);
        Assert.Equal([10], controlled.Select(static entity => entity.Id));
    }

    [Fact]
    public void ParsesZoneAndReturnsOrderedBoard()
    {
        var store = new EntityStore();
        ConfigureBoardEntity(store, 10, controller: 1, position: 2);
        ConfigureBoardEntity(store, 11, controller: 1, position: 1);
        ConfigureBoardEntity(store, 12, controller: 2, position: 1);
        _ = ApplyTag(store, 13, "ZONE", "HAND");

        var board = store.GetBoard(1);

        Assert.Equal(Zone.Play, store.Get(10).Zone);
        Assert.True(store.Get(10).IsInPlay);
        Assert.Equal([11, 10], board.Select(static entity => entity.Id));
        Assert.Equal(3, store.GetEntitiesInZone(Zone.Play).Count());
        Assert.True(store.Get(13).IsInHand);
    }

    [Fact]
    public void CalculatesHealthFromBaseHealthAndDamage()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 20, "HEALTH", "12");
        _ = ApplyTag(store, 20, "DAMAGE", "5");

        var entity = store.Get(20);

        Assert.Equal(12, entity.BaseHealth);
        Assert.Equal(5, entity.Damage);
        Assert.Equal(7, entity.Health);
    }

    [Fact]
    public void ParsesSymbolicTerminalPlayState()
    {
        var store = new EntityStore();

        var mutation = ApplyTag(store, 20, "PLAYSTATE", "WON");

        Assert.Equal(
            new EntityMutation(20, GameTag.PlayState, 0, (int)GamePlayState.Won),
            mutation);
    }

    [Fact]
    public void ExposesZonePositionAndCardTypeFlags()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 30, "ZONE_POSITION", "4");
        _ = ApplyTag(store, 30, "CARDTYPE", "MINION");

        var entity = store.Get(30);

        Assert.Equal(4, entity.ZonePosition);
        Assert.Equal(CardType.Minion, entity.CardType);
        Assert.True(entity.IsMinion);
        Assert.False(entity.IsHero);
    }

    [Fact]
    public void AppliesIdentityEventsAndPlayerId()
    {
        var store = new EntityStore();
        _ = store.Apply(new EntityCreated(Timestamp, null, 40, "CARD_001"));
        _ = store.Apply(new EntityRevealed(Timestamp, null, 40, "Test Entity", "CARD_002"));
        var playerMutation = store.Apply(
            new PlayerEntityDeclared(Timestamp, null, 41, 2, "account"));

        Assert.Equal("CARD_002", store.Get(40).CardId);
        Assert.Equal("Test Entity", store.Get(40).Name);
        Assert.Equal(new EntityMutation(41, GameTag.PlayerId, 0, 2), playerMutation);
        Assert.Equal(2, store.Get(41).PlayerId);
        Assert.True(store.Get(41).IsPlayer);
    }

    [Fact]
    public void RejectsIdentityTextThatExceedsRetentionLimits()
    {
        var store = new EntityStore();

        Assert.Throws<InvalidDataException>(() => store.Apply(
            new EntityCreated(
                Timestamp,
                null,
                42,
                new string('C', EntityStore.MaximumCardIdLength + 1))));
        Assert.Throws<InvalidDataException>(() => store.Apply(
            new EntityRevealed(
                Timestamp,
                null,
                42,
                new string('N', EntityStore.MaximumEntityNameLength + 1),
                "CARD_001")));
        Assert.False(store.TryGet(42, out _));
    }

    [Fact]
    public void SnapshotRemainsUnchangedAfterSourceMutation()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 50, "ATK", "4");
        var snapshot = store.CreateSnapshot(50);

        _ = ApplyTag(store, 50, "ATK", "9");

        Assert.Equal(4, snapshot.Attack);
        Assert.Equal(4, snapshot.Tags[GameTag.Attack]);
        Assert.Equal(9, store.Get(50).Attack);
    }

    [Fact]
    public void CreatesAllSnapshotsInStableEntityOrder()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 3, "ATK", "3");
        _ = ApplyTag(store, 1, "ATK", "1");

        var snapshots = store.CreateAllSnapshots();

        Assert.Equal([1, 3], snapshots.Select(static entity => entity.Id));
    }

    [Fact]
    public void ResetRemovesAllEntities()
    {
        var store = new EntityStore();
        _ = ApplyTag(store, 1, "ATK", "1");

        store.Reset();

        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.MaximumTagCount);
        Assert.False(store.TryGet(1, out _));
        Assert.Throws<KeyNotFoundException>(() => store.Get(1));
    }

    [Fact]
    public void AppliesSeveralThousandMutationsWithinDiagnosticBudget()
    {
        const int mutationCount = 20_000;
        var store = new EntityStore();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 1; index <= mutationCount; index++)
        {
            var mutation = ApplyTag(store, index, "ATK", "1");
            Assert.NotNull(mutation);
        }

        stopwatch.Stop();
        Assert.Equal(mutationCount, store.Count);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Applying {mutationCount} mutations took {stopwatch.Elapsed}.");
    }

    private static EntityMutation? ApplyTag(
        EntityStore store,
        int entityId,
        string tag,
        string value) =>
        store.Apply(
            new RawTagChanged(
                Timestamp,
                BlockId: null,
                EntityId: entityId,
                EntityName: null,
                Tag: tag,
                Value: value,
                IsCreationTag: false));

    private static void ConfigureBoardEntity(
        EntityStore store,
        int entityId,
        int controller,
        int position)
    {
        _ = ApplyTag(
            store,
            entityId,
            "CONTROLLER",
            controller.ToString(CultureInfo.InvariantCulture));
        _ = ApplyTag(store, entityId, "ZONE", "PLAY");
        _ = ApplyTag(
            store,
            entityId,
            "ZONE_POSITION",
            position.ToString(CultureInfo.InvariantCulture));
    }
}
