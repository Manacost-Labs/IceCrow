using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Entities;
using IceCrow.Tracking;

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
    public const long MaximumTimelineWorkUnits = 1_000_000;

    private readonly RecordedMatch _match;
    private readonly long _maximumStateMaterializationWorkUnits;
    private readonly long _maximumEventSnapshotWorkUnits;
    private readonly TrackingSession _tracking;
    private int _nextEventIndex;
    private int _opponentSnapshotCount;
    private long _snapshotWorkUnits;
    private long _eventSnapshotWorkUnits;
    private long _stateMaterializationWorkUnits;
    private long _timelineWorkUnits;
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
        _tracking = new TrackingSession(new TrackingSessionLimits(
            maximumTrackedEntities: MaximumReplayEntities,
            trackedEntityWarningThreshold: MaximumReplayEntities,
            maximumTagsPerEntity: TrackingSessionLimits.DefaultMaximumTagsPerEntity,
            tagsPerEntityWarningThreshold: TrackingSessionLimits.DefaultMaximumTagsPerEntity,
            maximumLobbyPlayers: MaximumLobbyPlayers,
            lobbyPlayerWarningThreshold: MaximumLobbyPlayers,
            maximumTimelineEventsPerPlayer: TrackingSessionLimits.DefaultMaximumTimelineEventsPerPlayer,
            timelineEventWarningThreshold: TrackingSessionLimits.DefaultMaximumTimelineEventsPerPlayer,
            maximumOpponentSnapshotsPerPlayer: MaximumOpponentSnapshots,
            opponentSnapshotWarningThreshold: MaximumOpponentSnapshots));
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
        _tracking.Reset();
        _nextEventIndex = 0;
        _opponentSnapshotCount = 0;
        _snapshotWorkUnits = 0;
        _eventSnapshotWorkUnits = 0;
        _stateMaterializationWorkUnits = 0;
        _timelineWorkUnits = 0;
        _isFaulted = false;
        _currentState = null;
    }

    private void Apply(RecordedEvent recordedEvent)
    {
        TrackingUpdate update;
        switch (recordedEvent.Type)
        {
            case RecordedEventType.MatchStarted:
                update = _tracking.StartBattlegroundsMatch(
                    recordedEvent.Timestamp,
                    recordedEvent.PlayerId);
                _opponentSnapshotCount = 0;
                _snapshotWorkUnits = 0;
                break;
            case RecordedEventType.MatchEnded:
                update = _tracking.EndMatch(recordedEvent.Timestamp);
                break;
            default:
                update = ApplyGameEvent(recordedEvent);
                break;
        }

        ValidateTrackingUpdate(update);
        ReserveTimelineWork();
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

    private TrackingUpdate ApplyGameEvent(RecordedEvent recordedEvent)
    {
        if (recordedEvent.EntityId is int newEntityId &&
            !_tracking.ContainsEntity(newEntityId) &&
            _tracking.EntityCount >= MaximumReplayEntities)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumReplayEntities} entity limit.");
        }

        TrackingUpdate update;
        try
        {
            update = _tracking.Apply(recordedEvent.ToGameEvent());
        }
        catch (TrackingSafetyLimitExceededException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }

        if (update.Entity is not null)
        {
            ReserveEventSnapshotWork(update.Entity);
        }

        return update;
    }

    private void ValidateTrackingUpdate(TrackingUpdate update)
    {
        if (update.Battlegrounds.Lobby.Count > MaximumLobbyPlayers)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumLobbyPlayers} lobby player limit.");
        }

        if (update.ObservedBoard is not BoardSnapshot observedBoard)
        {
            return;
        }

        if (_opponentSnapshotCount >= MaximumOpponentSnapshots)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumOpponentSnapshots} opponent snapshot limit.");
        }

        ReserveSnapshotWork(observedBoard);
        ValidateOpponentBoard(observedBoard);
        _opponentSnapshotCount++;
    }

    private ReplayState CreateState()
    {
        try
        {
            var work = checked(
                _tracking.EntitySnapshotWorkUnits +
                _tracking.TimelineEventCount +
                _tracking.OpponentSnapshotCount);
            if (_stateMaterializationWorkUnits > _maximumStateMaterializationWorkUnits - work)
            {
                throw new InvalidDataException(
                    $"Replay exceeds the {_maximumStateMaterializationWorkUnits} state materialization work-unit limit.");
            }

            _stateMaterializationWorkUnits += work;
            var tracking = _tracking.Current;
            return new ReplayState(
                CurrentEventIndex,
                _tracking.CreateEntitySnapshots(),
                tracking.Battlegrounds,
                tracking.OpponentMemory,
                tracking.LobbyTimeline);
        }
        catch
        {
            _isFaulted = true;
            throw;
        }
    }

    private void ReserveSnapshotWork(BoardSnapshot snapshot)
    {
        var work = snapshot.Minions.Sum(static minion => 1L + minion.Tags.Count);
        if (_snapshotWorkUnits > MaximumSnapshotWorkUnits - work)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumSnapshotWorkUnits} snapshot work-unit limit.");
        }

        _snapshotWorkUnits += work;
    }

    private void ReserveEventSnapshotWork(EntitySnapshot entity)
    {
        var work = 1L + entity.Tags.Count;
        if (_eventSnapshotWorkUnits > _maximumEventSnapshotWorkUnits - work)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {_maximumEventSnapshotWorkUnits} event snapshot work-unit limit.");
        }

        _eventSnapshotWorkUnits += work;
    }

    private void ReserveTimelineWork()
    {
        var work = 1L + _tracking.TimelineEventCount;
        if (_timelineWorkUnits > MaximumTimelineWorkUnits - work)
        {
            throw new InvalidDataException(
                $"Replay exceeds the {MaximumTimelineWorkUnits} timeline work-unit limit.");
        }

        _timelineWorkUnits += work;
    }

    private static void ValidateOpponentBoard(BoardSnapshot snapshot)
    {
        if (snapshot.Minions.Count > MaximumBoardMinions)
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
    OpponentMemory OpponentMemory,
    LobbyTimelineSnapshot LobbyTimeline)
{
    public int ProcessedEventCount => CurrentEventIndex + 1;
}
