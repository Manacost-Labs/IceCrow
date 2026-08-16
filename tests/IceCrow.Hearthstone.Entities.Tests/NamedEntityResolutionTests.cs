using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities.Tests;

/// <summary>
/// Real 2026 client logs reference most entities by bare name
/// (<c>Entity=GameEntity</c>, <c>Entity=PlayerName#1234</c>). Resolution may
/// only use proven associations; an ambiguous or unknown name is counted and
/// dropped instead of being applied to a guessed entity.
/// </summary>
public sealed class NamedEntityResolutionTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        23,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void BareGameEntityTurnTagResolvesToTheDeclaredGameEntity()
    {
        var store = new EntityStore();
        _ = store.Apply(new GameEntityDeclared(Timestamp, null, EntityId: 10));

        var mutation = store.Apply(NamedTag("GameEntity", "TURN", "1"));

        Assert.NotNull(mutation);
        Assert.Equal(10, mutation.EntityId);
        Assert.Equal(GameTag.Turn, mutation.Tag);
        Assert.Equal(1, mutation.Value);
        Assert.Equal(0, store.UnresolvedNamedReferences);
    }

    [Fact]
    public void DescriptorWithNameAndIdTeachesLaterBareNameReferences()
    {
        var store = new EntityStore();

        // A full descriptor line carries both the name and the id once...
        _ = store.Apply(new RawTagChanged(
            Timestamp,
            BlockId: null,
            EntityId: 2,
            EntityName: "Player#1234",
            Tag: "PLAYER_ID",
            Value: "4",
            IsCreationTag: false));

        // ...after which a bare-name line resolves to the same entity.
        var mutation = store.Apply(NamedTag("Player#1234", "PLAYSTATE", "WON"));

        Assert.NotNull(mutation);
        Assert.Equal(2, mutation.EntityId);
        Assert.Equal(GameTag.PlayState, mutation.Tag);
        Assert.Equal(0, store.UnresolvedNamedReferences);
    }

    [Fact]
    public void RevealedEntityNameTeachesBareNameReferences()
    {
        var store = new EntityStore();
        _ = store.Apply(new EntityRevealed(
            Timestamp,
            BlockId: null,
            EntityId: 236,
            EntityName: "Unique Hero",
            CardId: "BG_HERO_001"));

        var mutation = store.Apply(NamedTag("Unique Hero", "ARMOR", "15"));

        Assert.NotNull(mutation);
        Assert.Equal(236, mutation.EntityId);
    }

    [Fact]
    public void AmbiguousDuplicateNameNeverResolvesAndIsCounted()
    {
        var store = new EntityStore();
        _ = store.Apply(new EntityRevealed(
            Timestamp, null, 201, "Alleycat", "BG_CAT_001"));
        _ = store.Apply(new EntityRevealed(
            Timestamp, null, 202, "Alleycat", "BG_CAT_001"));

        var mutation = store.Apply(NamedTag("Alleycat", "ATK", "5"));

        Assert.Null(mutation);
        Assert.Equal(1, store.UnresolvedNamedReferences);
        Assert.Equal(0, store.Get(201).Attack);
        Assert.Equal(0, store.Get(202).Attack);
    }

    [Fact]
    public void UnknownNameIsCountedAndDropped()
    {
        var store = new EntityStore();

        var mutation = store.Apply(NamedTag("Never Declared", "HEALTH", "40"));

        Assert.Null(mutation);
        Assert.Equal(1, store.UnresolvedNamedReferences);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ResetClearsAssociationsAndCounters()
    {
        var store = new EntityStore();
        _ = store.Apply(new GameEntityDeclared(Timestamp, null, EntityId: 10));
        _ = store.Apply(NamedTag("Missing", "HEALTH", "1"));

        store.Reset();

        Assert.Null(store.GameEntityId);
        Assert.Equal(0, store.UnresolvedNamedReferences);
        Assert.Null(store.Apply(NamedTag("GameEntity", "TURN", "1")));
    }

    private static RawTagChanged NamedTag(string entityName, string tag, string value) => new(
        Timestamp,
        BlockId: null,
        EntityId: null,
        EntityName: entityName,
        Tag: tag,
        Value: value,
        IsCreationTag: false);
}
