using System.Collections.Frozen;

namespace IceCrow.Hearthstone.Data;

public sealed class InMemoryCardDatabase : ICardDatabase
{
    private DatabaseState _state = DatabaseState.Empty;

    public event EventHandler? DataUpdated;

    public HearthstoneDataVersion? Version => Volatile.Read(ref _state).Version;

    public int CardCount => Volatile.Read(ref _state).CardsById.Count;

    public int HeroCount => Volatile.Read(ref _state).HeroesById.Count;

    public CardDefinition? GetByCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        return Volatile.Read(ref _state).CardsById.GetValueOrDefault(cardId);
    }

    public CardDefinition? GetByDbfId(int dbfId) =>
        dbfId <= 0 ? null : Volatile.Read(ref _state).CardsByDbf.GetValueOrDefault(dbfId);

    public BattlegroundsHeroDefinition? GetHeroByCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        return Volatile.Read(ref _state).HeroesById.GetValueOrDefault(cardId);
    }

    public IReadOnlyList<CardDefinition> QueryBattlegroundsCards(CardQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var state = Volatile.Read(ref _state);
        return state.CardsById.Values
            .Where(card => query.TavernTier is null || card.TavernTier == query.TavernTier)
            .Where(card => query.IsInPool is null || card.IsInPool == query.IsInPool)
            .Where(card => query.IncludeDuosOnly || !card.IsDuosOnly)
            .Where(card => string.IsNullOrWhiteSpace(query.CreatureType) ||
                card.CreatureTypes.Contains(query.CreatureType, StringComparer.OrdinalIgnoreCase))
            .Where(card => string.IsNullOrWhiteSpace(query.SearchText) ||
                card.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                card.CardId.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(card => card.TavernTier)
            .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Replace(HearthstoneDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var cardsById = snapshot.Cards.ToFrozenDictionary(card => card.CardId, StringComparer.OrdinalIgnoreCase);
        var cardsByDbf = snapshot.Cards.ToFrozenDictionary(card => card.DbfId);
        var heroesById = snapshot.Heroes.ToFrozenDictionary(hero => hero.CardId, StringComparer.OrdinalIgnoreCase);
        Volatile.Write(ref _state, new DatabaseState(snapshot.Version, cardsById, cardsByDbf, heroesById));
        DataUpdated?.Invoke(this, EventArgs.Empty);
    }

    private sealed record DatabaseState(
        HearthstoneDataVersion? Version,
        FrozenDictionary<string, CardDefinition> CardsById,
        FrozenDictionary<int, CardDefinition> CardsByDbf,
        FrozenDictionary<string, BattlegroundsHeroDefinition> HeroesById)
    {
        public static DatabaseState Empty { get; } = new(
            null,
            FrozenDictionary<string, CardDefinition>.Empty,
            FrozenDictionary<int, CardDefinition>.Empty,
            FrozenDictionary<string, BattlegroundsHeroDefinition>.Empty);
    }
}
