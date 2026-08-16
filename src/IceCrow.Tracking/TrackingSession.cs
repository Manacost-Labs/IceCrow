using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;
using System.Globalization;

namespace IceCrow.Tracking;

public sealed class TrackingSession
{
    private readonly TrackingSessionLimits _limits;
    private readonly EntityStore _entities;
    private readonly OpponentMemoryService _opponentMemory;
    private readonly LobbyTimeline _lobbyTimeline;
    private BattlegroundsState _battlegrounds = BattlegroundsState.Empty;
    private TrackingSessionState _sessionState;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _endedAt;
    private long _revision;
    private TrackingSnapshot? _current;

    public TrackingSession(TrackingSessionLimits? limits = null)
    {
        _limits = limits ?? TrackingSessionLimits.Default;
        _entities = new EntityStore(
            _limits.MaximumTagsPerEntity,
            _limits.MaximumTotalTags);
        _opponentMemory = new OpponentMemoryService(
            _limits.MaximumLobbyPlayers,
            _limits.MaximumOpponentSnapshotsPerPlayer);
        _lobbyTimeline = new LobbyTimeline(
            _limits.MaximumLobbyPlayers,
            _limits.MaximumTimelineEventsPerPlayer);
    }

    public TrackingSnapshot Current => _current ??= CreateCurrent();

    public int EntityCount => _entities.Count;

    public int TagCount => _entities.TagCount;

    public int MaximumTagsOnEntity => _entities.MaximumTagCount;

    /// <summary>Named tag references that could not be resolved to a unique entity.</summary>
    public long UnresolvedNamedReferenceCount => _entities.UnresolvedNamedReferences;

    public int TimelineEventCount => _lobbyTimeline.EventCount;

    public int MaximumTimelineEventsOnPlayer => _lobbyTimeline.MaximumPlayerEventCount;

    public int OpponentSnapshotCount => _opponentMemory.SnapshotCount;

    public int MaximumOpponentSnapshotsOnPlayer => _opponentMemory.MaximumSnapshotCount;

    public TrackingSessionLimits Limits => _limits;

    public long EntitySnapshotWorkUnits => _entities.SnapshotWorkUnits;

    public TrackingUpdate StartBattlegroundsMatch(
        DateTimeOffset timestamp,
        int? localPlayerId = null)
    {
        var previousState = _sessionState;
        var previousPhase = _battlegrounds.Phase;

        ResetMatchState();
        _sessionState = TrackingSessionState.Active;
        _startedAt = timestamp;
        _battlegrounds = BattlegroundsReducer.Apply(
            BattlegroundsState.Empty,
            new BattlegroundsGameStarted(timestamp, localPlayerId));
        _lobbyTimeline.Update(_battlegrounds, timestamp);

        return CompleteUpdate(
            previousState,
            previousPhase,
            entityMutation: null,
            entity: null,
            observedBoard: null);
    }

    public TrackingUpdate Apply(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        if (_sessionState != TrackingSessionState.Active)
        {
            throw new InvalidOperationException(
                "A Battlegrounds match must be started before applying game events.");
        }

        EnsureEntityCapacity(gameEvent);
        EnsureLobbyCapacity(gameEvent);
        var previousPhase = _battlegrounds.Phase;
        EntityMutation? mutation;
        try
        {
            mutation = _entities.Apply(gameEvent);
        }
        catch (EntityTagLimitExceededException exception)
        {
            throw new TrackingSafetyLimitExceededException(
                TrackingSafetyLimit.TagsPerEntity,
                _limits.MaximumTagsPerEntity,
                exception.Message,
                exception);
        }
        catch (EntityStoreTagLimitExceededException exception)
        {
            throw new TrackingSafetyLimitExceededException(
                TrackingSafetyLimit.TotalTags,
                _limits.MaximumTotalTags,
                exception.Message,
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new TrackingSafetyLimitExceededException(
                TrackingSafetyLimit.RetainedText,
                EntityStore.MaximumEntityNameLength,
                exception.Message,
                exception);
        }
        var entity = TryCreateEventEntitySnapshot(gameEvent, mutation);
        if (entity is not null)
        {
            _battlegrounds = mutation is null
                ? BattlegroundsReducer.Apply(
                    _battlegrounds,
                    new BattlegroundsEntityObserved(gameEvent.Timestamp, entity))
                : BattlegroundsReducer.Apply(
                    _battlegrounds,
                    new BattlegroundsEntityChanged(gameEvent.Timestamp, entity, mutation));
        }

        var observedBoard = CaptureOpponentBoardOnCombatEntry(
            previousPhase,
            gameEvent.Timestamp);
        _lobbyTimeline.Update(_battlegrounds, gameEvent.Timestamp, observedBoard);

        return CompleteUpdate(
            TrackingSessionState.Active,
            previousPhase,
            mutation,
            entity,
            observedBoard);
    }

    public TrackingUpdate EndMatch(DateTimeOffset timestamp)
    {
        if (_sessionState != TrackingSessionState.Active)
        {
            throw new InvalidOperationException("There is no active match to end.");
        }

        var previousPhase = _battlegrounds.Phase;
        _battlegrounds = BattlegroundsReducer.Apply(
            _battlegrounds,
            new BattlegroundsGameEnded(timestamp));
        _lobbyTimeline.Update(_battlegrounds, timestamp);
        _sessionState = TrackingSessionState.Ended;
        _endedAt = timestamp;

        return CompleteUpdate(
            TrackingSessionState.Active,
            previousPhase,
            entityMutation: null,
            entity: null,
            observedBoard: null);
    }

    public void Reset()
    {
        ResetMatchState();
        _sessionState = TrackingSessionState.Inactive;
        _startedAt = null;
        _endedAt = null;
        _revision = 0;
        _current = null;
    }

    public bool ContainsEntity(int entityId) => _entities.TryGet(entityId, out _);

    public IReadOnlyList<EntitySnapshot> CreateEntitySnapshots() =>
        _entities.CreateAllSnapshots();

    private void ResetMatchState()
    {
        _entities.Reset();
        _opponentMemory.Reset();
        _lobbyTimeline.Reset();
        _battlegrounds = BattlegroundsState.Empty;
        _startedAt = null;
        _endedAt = null;
        _current = null;
    }

    private void EnsureEntityCapacity(GameEvent gameEvent)
    {
        if (TryGetEntityId(gameEvent) is not int entityId || ContainsEntity(entityId))
        {
            return;
        }

        if (_entities.Count >= _limits.MaximumTrackedEntities)
        {
            throw new TrackingSafetyLimitExceededException(
                TrackingSafetyLimit.TrackedEntities,
                _limits.MaximumTrackedEntities,
                $"Tracking session exceeds the {_limits.MaximumTrackedEntities} entity limit.");
        }
    }

    private void EnsureLobbyCapacity(GameEvent gameEvent)
    {
        if (TryGetProspectivePlayerId(gameEvent) is not int playerId ||
            playerId <= 0 ||
            _battlegrounds.Lobby.GetPlayer(playerId) is not null ||
            _battlegrounds.Lobby.Count < _limits.MaximumLobbyPlayers)
        {
            return;
        }

        throw new TrackingSafetyLimitExceededException(
            TrackingSafetyLimit.LobbyPlayers,
            _limits.MaximumLobbyPlayers,
            $"Tracking session exceeds the {_limits.MaximumLobbyPlayers} lobby-player limit.");
    }

    private EntitySnapshot? TryCreateEventEntitySnapshot(
        GameEvent gameEvent,
        EntityMutation? mutation)
    {
        // A mutation knows its entity even when the raw line referenced the
        // entity by bare name and carried no numeric id.
        var entityId = mutation?.EntityId ?? TryGetEntityId(gameEvent);
        if (entityId is not int id || !ContainsEntity(id))
        {
            return null;
        }

        return _entities.CreateSnapshot(id);
    }

    private BoardSnapshot? CaptureOpponentBoardOnCombatEntry(
        BattlegroundsPhase previousPhase,
        DateTimeOffset timestamp)
    {
        if (previousPhase == BattlegroundsPhase.Combat ||
            _battlegrounds.Phase != BattlegroundsPhase.Combat ||
            _battlegrounds.CurrentOpponentPlayerId is not int opponentPlayerId)
        {
            return null;
        }

        var opponentBoard = _entities.CreateBoardSnapshots(opponentPlayerId);
        return _opponentMemory.Capture(
            opponentPlayerId,
            _battlegrounds.Turn,
            opponentBoard,
            timestamp);
    }

    private TrackingUpdate CompleteUpdate(
        TrackingSessionState previousSessionState,
        BattlegroundsPhase previousPhase,
        EntityMutation? entityMutation,
        EntitySnapshot? entity,
        BoardSnapshot? observedBoard)
    {
        _revision = checked(_revision + 1);
        _current = null;
        return new TrackingUpdate(
            _revision,
            previousSessionState,
            _sessionState,
            previousPhase,
            _battlegrounds,
            entityMutation,
            entity,
            observedBoard);
    }

    private TrackingSnapshot CreateCurrent() => new(
        _revision,
        _sessionState,
        _startedAt,
        _endedAt,
        _entities.Count,
        _entities.TagCount,
        _entities.MaximumTagCount,
        _lobbyTimeline.EventCount,
        _lobbyTimeline.MaximumPlayerEventCount,
        _opponentMemory.SnapshotCount,
        _opponentMemory.MaximumSnapshotCount,
        _battlegrounds,
        _opponentMemory.Memory,
        _lobbyTimeline.CreateSnapshot());

    private static int? TryGetEntityId(GameEvent gameEvent) => gameEvent switch
    {
        GameEntityDeclared declared => declared.EntityId,
        PlayerEntityDeclared declared => declared.EntityId,
        EntityCreated created => created.EntityId,
        EntityRevealed revealed => revealed.EntityId,
        EntityChanged changed => changed.EntityId,
        RawTagChanged changed => changed.EntityId,
        _ => null,
    };

    private int? TryGetProspectivePlayerId(GameEvent gameEvent)
    {
        if (gameEvent is PlayerEntityDeclared player)
        {
            return player.PlayerId;
        }

        if (gameEvent is RawTagChanged tagChanged &&
            IsPlayerIdTag(tagChanged.Tag) &&
            int.TryParse(
                tagChanged.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var playerId))
        {
            return playerId;
        }

        return TryGetEntityId(gameEvent) is int entityId &&
               _entities.TryGet(entityId, out var entity)
            ? entity?.PlayerId
            : null;
    }

    private static bool IsPlayerIdTag(string tag) =>
        string.Equals(tag, "PLAYER_ID", StringComparison.Ordinal) ||
        string.Equals(tag, ((int)GameTag.PlayerId).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
