using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Constructed;

/// <summary>
/// Deterministic single-writer tracker for ranked Standard/Wild and Arena
/// games. It reduces the same normalized events as the Battlegrounds session
/// into a minimal bounded entity table and publishes one immutable
/// <see cref="ConstructedMatchSummary"/> per completed supported game; it never
/// touches the entity store, opponent memory, or board diffing.
/// <para>
/// Completion is deterministic: a game completes when its game entity reports
/// <c>STATE=COMPLETE</c>, carrying whatever terminal <c>PLAYSTATE</c> preceded
/// it (a terminal playstate after COMPLETE is ignored). A game still open at
/// the next <c>CREATE_GAME</c> is closed by that boundary and emitted only
/// when a result or progress beyond the initial turn was observed in a supported mode.
/// Game-scoped tags (<c>TURN</c>, <c>STATE</c>) are read from the declared
/// game entity, or from any entity while none was declared. Metadata that
/// arrives after a completed game and before the next boundary is kept for
/// the next game so either client ordering of the metadata block works.
/// </para>
/// </summary>
public sealed class ConstructedMatchTracker
{
    private readonly ConstructedEntityTable _entities;
    private readonly ConstructedGameObservation _observation;
    private GameMetadataState _metadata = GameMetadataState.Empty;
    private bool _metadataStale;
    private bool _gameOpen;
    private bool _modeRejected;
    private DateTimeOffset _startedAt;
    private int _turns;

    public ConstructedMatchTracker(ConstructedMatchLimits? limits = null)
    {
        limits ??= ConstructedMatchLimits.Default;
        _entities = new ConstructedEntityTable(limits.MaximumTrackedEntities);
        _observation = new ConstructedGameObservation(limits, _entities);
    }

    public GameMetadataState Metadata => _metadata;

    public bool IsGameOpen => _gameOpen;

    public int EntityCount => _entities.Count;

    public long RejectedEntities => _entities.RejectedEntities;

    public long GamesSeen { get; private set; }

    public long CompletedMatches { get; private set; }

    /// <summary>Games closed without a summary because their mode is not collected.</summary>
    public long IgnoredModeGames { get; private set; }

    public void Reset()
    {
        ClearGameState();
        _gameOpen = false;
        _metadata = GameMetadataState.Empty;
        _metadataStale = false;
    }

    public ConstructedTrackerUpdate Apply(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        switch (gameEvent)
        {
            case GameCreated created:
                return new ConstructedTrackerUpdate(StartGame(created), _metadata);
            case GameMetadataObserved observed:
                ApplyMetadata(observed);
                return new ConstructedTrackerUpdate(null, _metadata);
        }

        if (!_gameOpen || _modeRejected)
        {
            return new ConstructedTrackerUpdate(null, _metadata);
        }

        return new ConstructedTrackerUpdate(ApplyGameplay(gameEvent), _metadata);
    }

    private ConstructedMatchSummary? ApplyGameplay(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case GameEntityDeclared declared:
                _entities.DeclareGameEntity(declared.EntityId);
                break;
            case PlayerEntityDeclared player:
                DeclarePlayer(player.EntityId, player.PlayerId);
                break;
            case EntityCreated created:
                ApplyCardId(created.EntityId, null, created.CardId);
                break;
            case EntityRevealed revealed:
                ApplyCardId(revealed.EntityId, revealed.EntityName, revealed.CardId);
                break;
            case EntityChanged changed:
                ApplyCardId(changed.EntityId, changed.EntityName, changed.CardId);
                break;
            case BlockStarted started when started.Block.EntityId is int blockEntityId:
                _entities.AssociateName(started.Block.EntityName, blockEntityId);
                break;
            case RawTagChanged tagChanged:
                return ApplyTag(tagChanged);
        }

        return null;
    }

    private ConstructedMatchSummary? StartGame(GameCreated created)
    {
        var previous = _gameOpen ? CloseGame(created.Timestamp, closedByBoundary: true) : null;
        ClearGameState();
        if (_metadataStale)
        {
            _metadata = GameMetadataState.Empty;
            _metadataStale = false;
        }

        _gameOpen = true;
        _startedAt = created.Timestamp;
        GamesSeen = Increment(GamesSeen);
        RejectUncollectedMode();
        return previous;
    }

    private void ApplyMetadata(GameMetadataObserved observed)
    {
        if (_metadataStale)
        {
            _metadata = GameMetadataState.Empty;
            _metadataStale = false;
        }

        _metadata = _metadata.Apply(observed);
        RejectUncollectedMode();
    }

    /// <summary>
    /// A game whose metadata names a mode IceCrow does not collect keeps only
    /// its boundary: the table is freed, every later event is skipped, and the
    /// game is counted as ignored right here because its COMPLETE is never read.
    /// </summary>
    private void RejectUncollectedMode()
    {
        if (!_gameOpen || _modeRejected || _metadata.GameTypeToken is null || IsCollectedMode(_metadata.Mode))
        {
            return;
        }

        _modeRejected = true;
        IgnoredModeGames = Increment(IgnoredModeGames);
        _entities.Clear();
        _observation.Clear();
    }

    private ConstructedMatchSummary? ApplyTag(RawTagChanged tagChanged)
    {
        var entityId = _entities.Resolve(tagChanged.EntityId, tagChanged.EntityName);
        var tag = ConstructedTagVocabulary.ParseTag(tagChanged.Tag);
        if (entityId is not int id ||
            tag == ConstructedTag.None ||
            !ConstructedTagVocabulary.TryParseValue(tag, tagChanged.Value, out var value) ||
            _entities.GetOrAdd(id) is not { } entity)
        {
            return null;
        }

        switch (tag)
        {
            case ConstructedTag.Controller:
                entity.Controller = value;
                if (entity.OriginalController == 0)
                {
                    entity.OriginalController = value;
                }

                break;
            case ConstructedTag.Zone:
                entity.Zone = value;
                break;
            case ConstructedTag.CardType:
                entity.CardType = value;
                break;
            case ConstructedTag.ZonePosition:
                entity.ZonePosition = value;
                break;
            case ConstructedTag.PlayerId:
                _observation.DeclarePlayer(entity, value);
                break;
            case ConstructedTag.Turn when IsGameScoped(id):
                _turns = Math.Max(_turns, value);
                break;
            case ConstructedTag.PlayState:
                _observation.ObservePlayState(entity, value, tagChanged.Timestamp);
                break;
            case ConstructedTag.MulliganState:
                _observation.ObserveMulliganState(entity, value);
                break;
            case ConstructedTag.State when IsGameScoped(id) && value == (int)GameLifecycleState.Complete:
                return CloseGame(tagChanged.Timestamp, closedByBoundary: false);
        }

        _observation.Evaluate(entity, tagChanged.IsCreationTag);
        return null;
    }

    private void ApplyCardId(int? entityId, string? entityName, string cardId)
    {
        if (_entities.Resolve(entityId, entityName) is not int id ||
            _entities.GetOrAdd(id) is not { } entity)
        {
            return;
        }

        entity.CardId = cardId.Length is 0 or > ConstructedMatchLimits.MaximumCardIdLength ? null : cardId;
        _observation.Evaluate(entity, isCreationTag: false);
    }

    private void DeclarePlayer(int entityId, int playerId)
    {
        if (_entities.GetOrAdd(entityId) is { } entity)
        {
            _observation.DeclarePlayer(entity, playerId);
        }
    }

    private ConstructedMatchSummary? CloseGame(DateTimeOffset closedAt, bool closedByBoundary)
    {
        _gameOpen = false;
        _metadataStale = true;
        if (_modeRejected)
        {
            return null;
        }

        if (!IsSupported(_metadata))
        {
            IgnoredModeGames = Increment(IgnoredModeGames);
            return null;
        }

        // TURN=1 belongs to the initial game shell and also appears in client
        // transitions that never became a playable match. Without a result,
        // require evidence that play advanced beyond that shell.
        if (closedByBoundary && !_observation.HasResult && _turns <= 1)
        {
            return null;
        }

        CompletedMatches = Increment(CompletedMatches);
        return _observation.CreateSummary(_metadata, _startedAt, closedAt, _turns);
    }

    private bool IsGameScoped(int entityId) =>
        _entities.GameEntityId is not int gameEntityId || gameEntityId == entityId;

    private void ClearGameState()
    {
        _entities.Clear();
        _observation.Clear();
        _modeRejected = false;
        _turns = 0;
    }

    private static bool IsCollectedMode(GameMode mode) => mode is GameMode.Ranked or GameMode.Arena;

    private static bool IsSupported(GameMetadataState metadata) => metadata.Mode switch
    {
        GameMode.Ranked => metadata.Format is ConstructedFormat.Standard or ConstructedFormat.Wild,
        GameMode.Arena => true,
        _ => false,
    };

    private static long Increment(long counter) => counter < long.MaxValue ? counter + 1 : counter;
}
