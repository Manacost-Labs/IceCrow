namespace IceCrow.Hearthstone.Data;

public sealed class HearthstoneDataSnapshot
{
    private readonly CardDefinition[] _cards;
    private readonly BattlegroundsHeroDefinition[] _heroes;

    public HearthstoneDataSnapshot(
        HearthstoneDataVersion version,
        IEnumerable<CardDefinition> cards,
        IEnumerable<BattlegroundsHeroDefinition> heroes)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(heroes);

        Version = version;
        _cards = cards.ToArray();
        _heroes = heroes.ToArray();
        Cards = Array.AsReadOnly(_cards);
        Heroes = Array.AsReadOnly(_heroes);
    }

    public HearthstoneDataVersion Version { get; }

    public IReadOnlyList<CardDefinition> Cards { get; }

    public IReadOnlyList<BattlegroundsHeroDefinition> Heroes { get; }
}
