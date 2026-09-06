using System.Diagnostics.CodeAnalysis;

namespace IceCrow.Tracking.Constructed;

internal sealed class ConstructedEntity(int id)
{
    public int Id { get; } = id;

    public string? CardId { get; set; }

    public int Controller { get; set; }

    /// <summary>The first controller ever observed; never changes once set.</summary>
    public int OriginalController { get; set; }

    public int Zone { get; set; }

    public int CardType { get; set; }

    public int ZonePosition { get; set; }

    public int PlayerId { get; set; }

    public bool OpponentCardRecorded { get; set; }
}

/// <summary>
/// Minimal bounded entity table for Constructed tracking. Bare-name references
/// resolve only through a proven unique association, exactly as conservatively
/// as the full entity store: a name seen with two different ids is poisoned
/// and never resolves again within the game.
/// </summary>
internal sealed class ConstructedEntityTable(int maximumEntities)
{
    private const int AmbiguousName = -1;
    private const string GameEntityReference = "GameEntity";

    private readonly Dictionary<int, ConstructedEntity> _entities = [];
    private readonly Dictionary<string, int> _entityIdsByName = new(StringComparer.Ordinal);
    private bool _gameEntityNameAmbiguous;

    public int Count => _entities.Count;

    public long RejectedEntities { get; private set; }

    public int? GameEntityId { get; private set; }

    public Dictionary<int, ConstructedEntity>.ValueCollection Entities => _entities.Values;

    public void Clear()
    {
        _entities.Clear();
        _entityIdsByName.Clear();
        _gameEntityNameAmbiguous = false;
        GameEntityId = null;
    }

    public void DeclareGameEntity(int entityId)
    {
        GameEntityId = entityId;
        _ = GetOrAdd(entityId);
    }

    public ConstructedEntity? GetOrAdd(int entityId)
    {
        if (_entities.TryGetValue(entityId, out var existing))
        {
            return existing;
        }

        if (_entities.Count >= maximumEntities)
        {
            if (RejectedEntities < long.MaxValue)
            {
                RejectedEntities++;
            }

            return null;
        }

        var created = new ConstructedEntity(entityId);
        _entities.Add(entityId, created);
        return created;
    }

    public bool TryGet(int entityId, [NotNullWhen(true)] out ConstructedEntity? entity) =>
        _entities.TryGetValue(entityId, out entity);

    /// <summary>
    /// Resolves an event's entity reference. A descriptor carrying both an id
    /// and a name teaches the association; a bare name resolves only when it
    /// is proven unique.
    /// </summary>
    public int? Resolve(int? entityId, string? entityName)
    {
        if (entityId is int id)
        {
            AssociateName(entityName, id);
            return id;
        }

        return entityName is { Length: > 0 } ? ResolveName(entityName) : null;
    }

    public void AssociateName(string? entityName, int entityId)
    {
        if (entityName is null ||
            entityName.Length is 0 or > ConstructedMatchLimits.MaximumEntityNameLength)
        {
            return;
        }

        // "GameEntity" is a valid BattleTag: a player carrying it must poison
        // the literal shortcut instead of hijacking the game entity.
        if (string.Equals(entityName, GameEntityReference, StringComparison.Ordinal))
        {
            if (GameEntityId != entityId)
            {
                _gameEntityNameAmbiguous = true;
            }

            return;
        }

        if (_entityIdsByName.TryGetValue(entityName, out var existing))
        {
            if (existing != entityId && existing != AmbiguousName)
            {
                _entityIdsByName[entityName] = AmbiguousName;
            }
        }
        else if (_entityIdsByName.Count < ConstructedMatchLimits.MaximumTrackedEntityNames)
        {
            _entityIdsByName.Add(entityName, entityId);
        }
    }

    private int? ResolveName(string entityName)
    {
        if (string.Equals(entityName, GameEntityReference, StringComparison.Ordinal))
        {
            return _gameEntityNameAmbiguous ? null : GameEntityId;
        }

        return _entityIdsByName.TryGetValue(entityName, out var entityId) && entityId != AmbiguousName
            ? entityId
            : null;
    }
}
