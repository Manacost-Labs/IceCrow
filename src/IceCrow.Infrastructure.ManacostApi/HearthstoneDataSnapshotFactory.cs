using IceCrow.Hearthstone.Data;

namespace IceCrow.Infrastructure.ManacostApi;

public static class HearthstoneDataSnapshotFactory
{
    public static HearthstoneDataSnapshot Create(
        string dataVersion,
        string? hearthstoneBuild,
        IEnumerable<CardDefinition> cards,
        IEnumerable<BattlegroundsHeroDefinition> heroes,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataVersion);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(heroes);
        var cardArray = cards.ToArray();
        var heroArray = heroes.ToArray();
        var hash = SnapshotCodec.ComputeContentHash(cardArray, heroArray, dataVersion, hearthstoneBuild);
        return new HearthstoneDataSnapshot(
            new HearthstoneDataVersion(
                JsonHearthstoneDataStore.CurrentSchemaVersion,
                dataVersion,
                hearthstoneBuild,
                hash,
                createdAt ?? DateTimeOffset.UtcNow),
            cardArray,
            heroArray);
    }
}
