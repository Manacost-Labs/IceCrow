namespace IceCrow.Hearthstone.Data;

public interface ICardDatabase
{
    HearthstoneDataVersion? Version { get; }

    int CardCount { get; }

    int HeroCount { get; }

    CardDefinition? GetByCardId(string cardId);

    CardDefinition? GetByDbfId(int dbfId);

    BattlegroundsHeroDefinition? GetHeroByCardId(string cardId);

    IReadOnlyList<CardDefinition> QueryBattlegroundsCards(CardQuery query);
}
