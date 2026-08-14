using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities;

public sealed class EntityStore
{
    public const int DefaultMaximumTagsPerEntity = 256;
    public const int DefaultMaximumTotalTags = 1_000_000;

    private readonly Dictionary<int, GameEntity> _entities = [];
    private readonly int _maximumTagsPerEntity;
    private readonly int _maximumTotalTags;
    private int _tagCount;

    public EntityStore(
        int maximumTagsPerEntity = DefaultMaximumTagsPerEntity,
        int maximumTotalTags = DefaultMaximumTotalTags)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTagsPerEntity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalTags);
        _maximumTagsPerEntity = maximumTagsPerEntity;
        _maximumTotalTags = maximumTotalTags;
    }

    public int Count => _entities.Count;

    public int TagCount => _tagCount;

    public int MaximumTagCount => _entities.Count == 0
        ? 0
        : _entities.Values.Max(static entity => entity.Tags.Count);

    public long SnapshotWorkUnits
    {
        get
        {
            long total = 0;
            foreach (var entity in _entities.Values)
            {
                total = checked(total + 1 + entity.Tags.Count);
            }

            return total;
        }
    }

    public GameEntity Get(int id) => _entities[id];

    public bool TryGet(int id, out GameEntity? entity) => _entities.TryGetValue(id, out entity);

    public GameEntity GetOrCreate(int id)
    {
        if (!_entities.TryGetValue(id, out var entity))
        {
            entity = new GameEntity(id, _maximumTagsPerEntity);
            _entities.Add(id, entity);
        }

        return entity;
    }

    public EntityMutation? Apply(GameEvent gameEvent) => EntityStoreReducer.Apply(this, gameEvent);

    internal EntityMutation? ApplyTag(int entityId, GameTag tag, int value)
    {
        var hasEntity = _entities.TryGetValue(entityId, out var entity);
        var isNewTag = !hasEntity || !entity!.Tags.ContainsKey(tag);
        if (isNewTag && _tagCount >= _maximumTotalTags)
        {
            throw new EntityStoreTagLimitExceededException(_maximumTotalTags);
        }

        entity ??= GetOrCreate(entityId);
        var mutation = entity.ApplyTag(tag, value);
        if (mutation is not null && isNewTag)
        {
            _tagCount = checked(_tagCount + 1);
        }

        return mutation;
    }

    public void Reset()
    {
        _entities.Clear();
        _tagCount = 0;
    }

    public EntitySnapshot CreateSnapshot(int id) => new(Get(id));

    public IReadOnlyList<EntitySnapshot> CreateAllSnapshots()
    {
        var snapshots = _entities.Values
            .OrderBy(static entity => entity.Id)
            .Select(static entity => new EntitySnapshot(entity))
            .ToArray();
        return Array.AsReadOnly(snapshots);
    }

    public IReadOnlyList<EntitySnapshot> CreateBoardSnapshots(int controllerId)
    {
        var snapshots = _entities.Values
            .Where(entity =>
                entity.IsMinion &&
                entity.IsInPlay &&
                entity.Controller == controllerId)
            .OrderBy(static entity => entity.ZonePosition)
            .ThenBy(static entity => entity.Id)
            .Select(static entity => new EntitySnapshot(entity))
            .ToArray();
        return Array.AsReadOnly(snapshots);
    }

    public IEnumerable<GameEntity> GetEntitiesByController(int controllerId)
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.Controller == controllerId)
            {
                yield return entity;
            }
        }
    }

    public IEnumerable<GameEntity> GetEntitiesInZone(Zone zone)
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.Zone == zone)
            {
                yield return entity;
            }
        }
    }

    public IReadOnlyList<GameEntity> GetBoard(int controllerId)
    {
        var board = new List<GameEntity>();
        foreach (var entity in _entities.Values)
        {
            if (entity.Controller == controllerId && entity.IsInPlay)
            {
                board.Add(entity);
            }
        }

        board.Sort(static (left, right) =>
        {
            var positionComparison = left.ZonePosition.CompareTo(right.ZonePosition);
            return positionComparison != 0 ? positionComparison : left.Id.CompareTo(right.Id);
        });
        return board;
    }
}
