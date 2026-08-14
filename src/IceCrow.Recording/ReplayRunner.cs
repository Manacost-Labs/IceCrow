using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;

namespace IceCrow.Recording;

public sealed class ReplayRunner
{
    public const int MaximumReplayEntities = 4_096;
    public const int MaximumLobbyPlayers = 16;
    public const int MaximumBoardMinions = 7;
    public const int MaximumOpponentSnapshots = 64;
    public const long MaximumSnapshotWorkUnits = 1_000_000;
    public const long MaximumEventSnapshotWorkUnits = 1_000_000;
    public const long MaximumStateMaterializationWorkUnits = 10_000_000;

    private readonly RecordedMatch _match;
    private readonly long _maximumStateMaterializationWorkUnits;
    private readonly long _maximumEventSnapshotWorkUnits;
    private readonly EntityStore _entities = new();
    private readonly OpponentMemoryService _opponentMemory = new();
    private BattlegroundsState _battlegrounds = BattlegroundsState.Empty;
    private int _nextEventIndex;
    private int _opponentSnapshotCount;
    private long _snapshotWorkUnits;
    private long _eventSnapshotWorkUnits;
    private long _stateMaterializationWorkUnits;
    private bool _isFaulted;
    private ReplayState? _currentState;

    public ReplayRunner(
        RecordedMatch match,
        long maximumStateMaterializationWorkUnits = MaximumStateMaterializationWorkUnits,
        long maximumEventSnapshotWorkUnits = MaximumEventSnapshotWorkUnits)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStateMaterializationWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEventSnapshotWorkUnits);
        RecordingSerializer.Validate(match);
        _match = match;
        _maximumStateMaterializationWorkUnits = maximumStateMaterializationWorkUnits;
        _maximumEventSnapshotWorkUnits = maximumEventSnapshotWorkUnits;
    }

    public bool CanStep => !_isFaulted && _nextEventIndex < _match.Events.Count;

    public int CurrentEventIndex => _nextEventIndex - 1;

    public ReplayState Current => _currentState ??= CreateState();

    public ReplayState Step(CancellationToken cancellationToken = default)
    {
        ThrowIfFaulted();
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanStep)
        {
            throw new InvalidOperationException("Replay is already at the end of the recording.");
        }

        StepCore();
        return CreateState();
    }

    public ReplayState RunAll(CancellationToken cancellationToken = default)
    {
        ThrowIfFaulted();
        while (CanStep)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StepCore();
        }

        return CreateState();
    }

    public ReplayState RunUntilEvent(
        int eventIndex,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFaulted();
        if (eventIndex < 0 || eventIndex >= _match.Events.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventIndex),
                eventIndex,
                "Event index must refer to an event in the recording.");
        }

        if (eventIndex < CurrentEventIndex)
        {
            Reset();
        }

        while (CurrentEventIndex < eventIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StepCore();
        }

        return CreateState();
    }

    public ReplayState RunToCheckpoint(
        string checkpointName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);
        var checkpoint = _match.Checkpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, checkpointName, StringComparison.Ordinal));
        if (checkpoint is null)
        {
            throw new KeyNotFoundException(
                $"Recording does not contain checkpoint '{checkpointName}'.");
        }

        return RunUntilEvent(checkpoint.EventIndex, cancellationToken);
    }

    public void Reset()
    {
        _entities.Reset();
        _opponentMemory.Reset();
        _battlegrounds = BattlegroundsState.Empty;
        _nextEventIndex = 0;
        _opponentSnapshotCount = 0;
        _snapshotWorkUnits = 0;
        _eventSnapshotWorkUnits = 0;
        _stateMaterializationWorkUnits = 0;
        _isFaulted = false;
        _currentState = null;
    }

    private void Apply(RecordedEvent recordedEvent)
    {
        var previousPhase = _battlegrounds.Phase;
        switch (recordedEvent.Type)
        {
            case RecordedEventType.MatchStarted:
                _entities.Reset();
                _opponentMemory.Reset();
                _battlegrounds = BattlegroundsReducer.Apply(
                    BattlegroundsState.Empty,
                    new BattlegroundsGameStarted(
                        recordedEvent.Timestamp,
                        recordedEvent.PlayerId));
                _opponentSnapshotCount = 0;
                _snapshotWorkUnits = 0;
                break;
            case RecordedEventType.MatchEnded:
                _battlegrounds = BattlegroundsReducer.Apply(
                    _battlegrounds,
                    new BattlegroundsGameEnded(recordedEvent.Timestamp));
                break;
            default:
                ApplyGameEvent(recordedEvent);
                break;
        }

        var enteringCombat = previousPhase != BattlegroundsPhase.Combat &&
                             _battlegrounds.Phase == BattlegroundsPhase.Combat;
        IReadOnlyList<EntitySnapshot> snapshots = [];
        if (enteringCombat)
        {
            if (_opponentSnapshotCount >= MaximumOpponentSnapshots)
            {
                throw new InvalidDataException(
                    $"Replay exceeds the {MaximumOpponentSnapshots} opponent snapshot limit.");
            }

            snapshots = _entities.CreateAllSnapshots();
            ReserveSnapshotWork(snapshots);
            ValidateOpponentBoard(snapshots);
            _opponentSnapshotCount++;
        }

        _ = _opponentMemory.Update(
            _battlegrounds,
            snapshots,
            recordedEvent.Timestamp);
    }

    private void StepCore()
    {
        try
        {
            Apply(_match.Events[_nextEventIndex]);
            _nextEventIndex++;
            _currentState = null;
        }
        catch
        {
            _isFaulted = true;
            throw;
        }
    }

    private void ApplyGameEvent(RecordedEvent recordedEvent)
    {
        if (recordedEvent.EntityId is int newEntityId &&
            !_entities.TryGet(newEntityId, out _) &&
            _entities.Count >= MaximumReplayEntities)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumReplayEntities} entity limit.");
        }

        var gameEvent = recordedEvent.ToGameEvent();
        var mutation = _entities.Apply(gameEvent);
        if (recordedEvent.EntityId is not int entityId ||
            !_entities.TryGet(entityId, out var entity) ||
            entity is null)
        {
            return;
        }

        ReserveEventSnapshotWork(entity);
        var snapshot = _entities.CreateSnapshot(entityId);
        if (snapshot.PlayerId > 0 &&
            _battlegrounds.Lobby.GetPlayer(snapshot.PlayerId) is null &&
            _battlegrounds.Lobby.Count >= MaximumLobbyPlayers)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumLobbyPlayers} lobby player limit.");
        }

        _battlegrounds = mutation is null
            ? BattlegroundsReducer.Apply(
                _battlegrounds,
                new BattlegroundsEntityObserved(recordedEvent.Timestamp, snapshot))
            : BattlegroundsReducer.Apply(
                _battlegrounds,
                new BattlegroundsEntityChanged(recordedEvent.Timestamp, snapshot, mutation));
    }

    private ReplayState CreateState()
    {
        try
        {
            var work = _entities.SnapshotWorkUnits;
            if (_stateMaterializationWorkUnits > _maximumStateMaterializationWorkUnits - work)
            {
                throw new InvalidDataException(
                    $"Replay exceeds the {_maximumStateMaterializationWorkUnits} state materialization work-unit limit.");
            }

            _stateMaterializationWorkUnits += work;
            return new ReplayState(
                CurrentEventIndex,
                _entities.CreateAllSnapshots(),
                _battlegrounds,
                _opponentMemory.Memory);
        }
        catch
        {
            _isFaulted = true;
            throw;
        }
    }

    private void ReserveSnapshotWork(IReadOnlyList<EntitySnapshot> snapshots)
    {
        var work = snapshots.Sum(static snapshot => 1L + snapshot.Tags.Count);
        if (_snapshotWorkUnits > MaximumSnapshotWorkUnits - work)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumSnapshotWorkUnits} snapshot work-unit limit.");
        }

        _snapshotWorkUnits += work;
    }

    private void ReserveEventSnapshotWork(GameEntity entity)
    {
        var work = 1L + entity.Tags.Count;
        if (_eventSnapshotWorkUnits > _maximumEventSnapshotWorkUnits - work)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {_maximumEventSnapshotWorkUnits} event snapshot work-unit limit.");
        }

        _eventSnapshotWorkUnits += work;
    }

    private void ValidateOpponentBoard(IReadOnlyList<EntitySnapshot> snapshots)
    {
        if (_battlegrounds.CurrentOpponentPlayerId is not int opponentPlayerId)
        {
            return;
        }

        var minionCount = snapshots.Count(snapshot =>
            snapshot.IsMinion &&
            snapshot.IsInPlay &&
            snapshot.Controller == opponentPlayerId);
        if (minionCount > MaximumBoardMinions)
        {
            throw new InvalidDataException(
                $"Replay opponent board exceeds the {MaximumBoardMinions} minion limit.");
        }
    }

    private void ThrowIfFaulted()
    {
        if (_isFaulted)
        {
            throw new InvalidOperationException(
                "Replay cannot continue after an event failed. Reset it before replaying again.");
        }
    }
}

public sealed record ReplayState(
    int CurrentEventIndex,
    IReadOnlyList<EntitySnapshot> Entities,
    BattlegroundsState Battlegrounds,
    OpponentMemory OpponentMemory)
{
    public int ProcessedEventCount => CurrentEventIndex + 1;
}
