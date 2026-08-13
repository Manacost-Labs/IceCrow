using System.Collections.Frozen;

namespace IceCrow.Hearthstone.Entities;

public sealed class EntitySnapshot
{
    internal EntitySnapshot(GameEntity entity)
    {
        Id = entity.Id;
        CardId = entity.CardId;
        Name = entity.Name;
        Tags = entity.Tags.ToFrozenDictionary();
    }

    public int Id { get; }

    public string? CardId { get; }

    public string? Name { get; }

    public IReadOnlyDictionary<GameTag, int> Tags { get; }

    public CardType CardType => (CardType)GetTag(GameTag.CardType);

    public int Controller => GetTag(GameTag.Controller);

    public Zone Zone => (Zone)GetTag(GameTag.Zone);

    public int ZonePosition => GetTag(GameTag.ZonePosition);

    public int Attack => GetTag(GameTag.Attack);

    public int BaseHealth => GetTag(GameTag.Health);

    public int Damage => GetTag(GameTag.Damage);

    public int Health => BaseHealth - Damage;

    public int PlayerId => GetTag(GameTag.PlayerId);

    public bool IsPlayer => PlayerId > 0;

    public bool IsHero => CardType == CardType.Hero;

    public bool IsMinion => CardType == CardType.Minion;

    public bool IsInPlay => Zone == Zone.Play;

    public bool IsInHand => Zone == Zone.Hand;

    public bool IsInSetAside => Zone == Zone.SetAside;

    public int GetTag(GameTag tag) => Tags.TryGetValue(tag, out var value) ? value : 0;
}
