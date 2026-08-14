namespace IceCrow.Hearthstone.Decks;

public sealed class DeckDefinition
{
    private readonly int[] _heroes;
    private readonly DeckCard[] _cards;
    private readonly DeckSideboardCard[] _sideboardCards;

    public DeckDefinition(
        DeckFormat format,
        IEnumerable<int> heroes,
        IEnumerable<DeckCard> cards,
        IEnumerable<DeckSideboardCard>? sideboardCards = null)
    {
        ArgumentNullException.ThrowIfNull(heroes);
        ArgumentNullException.ThrowIfNull(cards);
        Format = format;
        _heroes = heroes.ToArray();
        _cards = cards.ToArray();
        _sideboardCards = sideboardCards?.ToArray() ?? [];
        Heroes = Array.AsReadOnly(_heroes);
        Cards = Array.AsReadOnly(_cards);
        SideboardCards = Array.AsReadOnly(_sideboardCards);
    }

    public DeckFormat Format { get; }

    public IReadOnlyList<int> Heroes { get; }

    public IReadOnlyList<DeckCard> Cards { get; }

    public IReadOnlyList<DeckSideboardCard> SideboardCards { get; }
}
