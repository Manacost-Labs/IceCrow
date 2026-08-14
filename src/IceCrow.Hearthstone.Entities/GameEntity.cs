using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.Entities;

public sealed class GameEntity
{
    private readonly Dictionary<GameTag, int> _tags = [];
    private readonly int _maximumTags;
    private readonly ReadOnlyDictionary<GameTag, int> _readOnlyTags;

    internal GameEntity(int id, int maximumTags)
    {
        Id = id;
        _maximumTags = maximumTags;
        _readOnlyTags = new ReadOnlyDictionary<GameTag, int>(_tags);
    }

    public int Id { get; }

    public string? CardId { get; internal set; }

    public string? Name { get; internal set; }

    public IReadOnlyDictionary<GameTag, int> Tags => _readOnlyTags;

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

    public int GetTag(GameTag tag) => _tags.TryGetValue(tag, out var value) ? value : 0;

    internal EntityMutation? ApplyTag(GameTag tag, int value)
    {
        var previousValue = GetTag(tag);
        if (previousValue == value)
        {
            return null;
        }

        if (!_tags.ContainsKey(tag) && _tags.Count >= _maximumTags)
        {
            throw new EntityTagLimitExceededException(Id, _maximumTags);
        }

        _tags[tag] = value;
        return new EntityMutation(Id, tag, previousValue, value);
    }
}
