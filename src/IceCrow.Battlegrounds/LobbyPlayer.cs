namespace IceCrow.Battlegrounds;

public sealed record LobbyPlayer(
    int PlayerId,
    int? HeroEntityId,
    string? HeroName,
    string? HeroCardId,
    int Health,
    int Armor,
    int TavernTier,
    int Triples,
    bool IsAlive)
{
    public static LobbyPlayer Create(int playerId) => new(
        PlayerId: playerId,
        HeroEntityId: null,
        HeroName: null,
        HeroCardId: null,
        Health: 0,
        Armor: 0,
        TavernTier: 0,
        Triples: 0,
        IsAlive: true);
}
