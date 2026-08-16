using IceCrow.Hearthstone.Data;
using Xunit;

namespace IceCrow.Hearthstone.Data.Tests;

public sealed class InMemoryCardDatabaseTests
{
    [Fact]
    public void ReplaceSupportsIdentityLookupsAndUnknownCards()
    {
        var database = new InMemoryCardDatabase();
        database.Replace(Snapshot(Card("BG_TEST", 42, tier: 3, creatureType: "demon")));

        Assert.Equal("BG_TEST", database.GetByDbfId(42)?.CardId);
        Assert.Equal(42, database.GetByCardId("bg_test")?.DbfId);
        Assert.Null(database.GetByCardId("NEW_PATCH_CARD"));
        Assert.Null(database.GetByDbfId(999999));
    }

    [Fact]
    public void LocalBattlegroundsQueriesUseTheInMemorySnapshot()
    {
        var database = new InMemoryCardDatabase();
        database.Replace(Snapshot(
            Card("A", 1, tier: 2, creatureType: "beast"),
            Card("B", 2, tier: 2, creatureType: "demon"),
            Card("C", 3, tier: 4, creatureType: "beast", inPool: false)));

        var result = database.QueryBattlegroundsCards(new CardQuery(
            TavernTier: 2,
            CreatureType: "beast",
            IsInPool: true));

        Assert.Collection(result, card => Assert.Equal("A", card.CardId));
    }

    [Fact]
    public void SnapshotCopiesCallerOwnedCollections()
    {
        var cards = new List<CardDefinition> { Card("A", 1, 1, "beast") };
        var snapshot = new HearthstoneDataSnapshot(
            new HearthstoneDataVersion(1, "v1", null, new string('0', 64), DateTimeOffset.UnixEpoch),
            cards,
            []);
        cards.Clear();

        Assert.Single(snapshot.Cards);
    }

    [Fact]
    public void HeroLookupResolvesSkinCardIdsToTheBaseHeroDefinition()
    {
        var database = new InMemoryCardDatabase();
        database.Replace(SnapshotWithHeroes(
            Hero("BG22_HERO_000", 100, "Ragnaros"),
            Hero("TB_BaconShop_HERO_25", 101, "Yogg-Saron")));

        Assert.Equal("Ragnaros", database.GetHeroByCardId("BG22_HERO_000")?.Name);
        Assert.Equal("Ragnaros", database.GetHeroByCardId("BG22_HERO_000_SKIN_E")?.Name);
        Assert.Equal("Yogg-Saron", database.GetHeroByCardId("TB_BaconShop_HERO_25")?.Name);
        Assert.Null(database.GetHeroByCardId("BG99_HERO_UNKNOWN"));
        Assert.Null(database.GetHeroByCardId("BG99_HERO_UNKNOWN_SKIN_B"));
    }

    [Fact]
    public void SkinNormalizationOnlyStripsTheDocumentedSuffixPattern()
    {
        Assert.True(BattlegroundsHeroSkins.TryGetBaseHeroCardId(
            "BG22_HERO_000_SKIN_E",
            out var baseId));
        Assert.Equal("BG22_HERO_000", baseId);

        Assert.False(BattlegroundsHeroSkins.TryGetBaseHeroCardId("BG22_HERO_000", out _));
        Assert.False(BattlegroundsHeroSkins.TryGetBaseHeroCardId("_SKIN_E", out _));
        Assert.False(BattlegroundsHeroSkins.TryGetBaseHeroCardId("BG22_HERO_000_SKIN_", out _));
        Assert.False(BattlegroundsHeroSkins.TryGetBaseHeroCardId(
            "BG22_HERO_000_SKIN_e",
            out _));
    }

    private static HearthstoneDataSnapshot Snapshot(params CardDefinition[] cards) => new(
        new HearthstoneDataVersion(1, "v1", null, new string('0', 64), DateTimeOffset.UnixEpoch),
        cards,
        []);

    private static HearthstoneDataSnapshot SnapshotWithHeroes(
        params BattlegroundsHeroDefinition[] heroes) => new(
        new HearthstoneDataVersion(1, "v1", null, new string('0', 64), DateTimeOffset.UnixEpoch),
        [],
        heroes);

    private static BattlegroundsHeroDefinition Hero(string cardId, int dbfId, string name) => new(
        dbfId,
        cardId,
        name,
        name,
        null,
        null,
        null,
        null,
        null,
        CardImageInfo.Empty);

    private static CardDefinition Card(
        string cardId,
        int dbfId,
        int? tier,
        string creatureType,
        bool inPool = true) => new(
            dbfId,
            cardId,
            cardId,
            null,
            null,
            "minion",
            null,
            tier,
            new[] { creatureType },
            inPool,
            false,
            null,
            CardImageInfo.Empty);
}
