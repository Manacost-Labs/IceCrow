using System.Collections.Frozen;
using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds.Memory;

public sealed class MinionSnapshot
{
    private MinionSnapshot(EntitySnapshot entity)
    {
        EntityId = entity.Id;
        CardId = entity.CardId;
        Attack = entity.Attack;
        Health = entity.Health;
        ZonePosition = entity.ZonePosition;
        Tags = entity.Tags.ToFrozenDictionary();
    }

    public int EntityId { get; }

    public string? CardId { get; }

    public int Attack { get; }

    public int Health { get; }

    public int ZonePosition { get; }

    public IReadOnlyDictionary<GameTag, int> Tags { get; }

    public static MinionSnapshot FromEntity(EntitySnapshot entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!entity.IsMinion || !entity.IsInPlay)
        {
            throw new ArgumentException(
                "Only in-play minions can be captured in a board snapshot.",
                nameof(entity));
        }

        return new MinionSnapshot(entity);
    }
}
