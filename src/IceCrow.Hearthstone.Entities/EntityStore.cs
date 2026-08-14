using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities;

public sealed class EntityStore
{
    private readonly Dictionary<int, GameEntity> _entities = [];

    public int Count => _entities.Count;

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
            entity = new GameEntity(id);
            _entities.Add(id, entity);
        }

        return entity;
    }

    public EntityMutation? Apply(GameEvent gameEvent) => EntityStoreReducer.Apply(this, gameEvent);

    public void Reset() => _entities.Clear();

    public EntitySnapshot CreateSnapshot(int id) => new(Get(id));

    public IReadOnlyList<EntitySnapshot> CreateAllSnapshots()
    {
        var snapshots = _entities.Values
            .OrderBy(static entity => entity.Id)
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
