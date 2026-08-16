using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities;

public sealed class EntityStore
{
    public const int DefaultMaximumTagsPerEntity = 256;
    public const int DefaultMaximumTotalTags = 1_000_000;
    public const int MaximumCardIdLength = 128;
    public const int MaximumEntityNameLength = 1_024;
    public const int MaximumTrackedEntityNames = 8_192;

    // The real client references the game entity by this literal instead of
    // a numeric id in most TAG_CHANGE lines.
    private const string GameEntityReference = "GameEntity";
    private const int AmbiguousEntityName = -1;

    private readonly Dictionary<int, GameEntity> _entities = [];
    private readonly Dictionary<string, int> _entityIdsByName = new(StringComparer.Ordinal);
    private readonly int _maximumTagsPerEntity;
    private readonly int _maximumTotalTags;
    private int? _gameEntityId;
    private bool _gameEntityNameAmbiguous;
    private long _unresolvedNamedReferences;
    private int _tagCount;
    private int _maximumTagCount;

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

    public int? GameEntityId => _gameEntityId;

    /// <summary>
    /// Named entity references that could not be resolved to a unique entity
    /// id. Unresolved changes are dropped rather than applied to a guessed
    /// entity, so this counter is the honest measure of lost evidence.
    /// </summary>
    public long UnresolvedNamedReferences => _unresolvedNamedReferences;

    public int TagCount => _tagCount;

    public int MaximumTagCount => _maximumTagCount;

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

    public EntityMutation? Apply(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ValidateRetainedText(gameEvent);
        return EntityStoreReducer.Apply(this, gameEvent);
    }

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
            _maximumTagCount = Math.Max(_maximumTagCount, entity.Tags.Count);
        }

        return mutation;
    }

    internal void DeclareGameEntity(int entityId)
    {
        _gameEntityId = entityId;
        _ = GetOrCreate(entityId);
    }

    /// <summary>
    /// Remembers a name-to-id association proven by an authoritative line
    /// that carried both. A name claimed by two different ids becomes
    /// permanently ambiguous and never resolves again.
    /// </summary>
    internal void AssociateEntityName(string entityName, int entityId)
    {
        if (entityName.Length is 0 or > MaximumEntityNameLength)
        {
            return;
        }

        // "GameEntity" is a valid BattleTag: a player carrying it must poison
        // the literal shortcut or their tags would be misattributed to the
        // game entity with false certainty.
        if (string.Equals(entityName, GameEntityReference, StringComparison.Ordinal))
        {
            if (_gameEntityId != entityId)
            {
                _gameEntityNameAmbiguous = true;
            }

            return;
        }

        if (_entityIdsByName.TryGetValue(entityName, out var existing))
        {
            if (existing != entityId && existing != AmbiguousEntityName)
            {
                _entityIdsByName[entityName] = AmbiguousEntityName;
            }
        }
        else if (_entityIdsByName.Count < MaximumTrackedEntityNames)
        {
            _entityIdsByName.Add(entityName, entityId);
        }
    }

    internal int? ResolveEntityName(string entityName)
    {
        if (string.Equals(entityName, GameEntityReference, StringComparison.Ordinal))
        {
            return _gameEntityNameAmbiguous ? null : _gameEntityId;
        }

        return _entityIdsByName.TryGetValue(entityName, out var entityId) &&
               entityId != AmbiguousEntityName
            ? entityId
            : null;
    }

    internal void CountUnresolvedNamedReference() =>
        _unresolvedNamedReferences++;

    public void Reset()
    {
        _entities.Clear();
        _entityIdsByName.Clear();
        _gameEntityId = null;
        _gameEntityNameAmbiguous = false;
        _unresolvedNamedReferences = 0;
        _tagCount = 0;
        _maximumTagCount = 0;
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

    private static void ValidateRetainedText(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case EntityCreated created:
                ValidateLength(created.CardId, MaximumCardIdLength, "CardId");
                break;
            case EntityRevealed revealed:
                ValidateLength(revealed.CardId, MaximumCardIdLength, "CardId");
                ValidateLength(revealed.EntityName, MaximumEntityNameLength, "entity name");
                break;
            case EntityChanged changed:
                ValidateLength(changed.CardId, MaximumCardIdLength, "CardId");
                ValidateLength(changed.EntityName, MaximumEntityNameLength, "entity name");
                break;
        }
    }

    private static void ValidateLength(string? value, int maximumLength, string field)
    {
        if (value?.Length > maximumLength)
        {
            throw new InvalidDataException($"The {field} exceeds the {maximumLength}-character retention limit.");
        }
    }
}
