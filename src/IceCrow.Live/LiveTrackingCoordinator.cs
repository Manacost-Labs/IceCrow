using System.Threading.Channels;
using IceCrow.Battlegrounds;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Live;

/// <summary>
/// Single-consumer live orchestration. Parsed events are applied directly to the
/// authoritative TrackingSession; no second queue is introduced.
/// </summary>
public sealed class LiveTrackingCoordinator
{
    public const int DefaultPendingEventCapacity = 512;
    public const int MaximumPendingEventCapacity = 4096;

    private readonly PowerLineParser _parser;
    private readonly TrackingSession _tracking;
    private readonly BattlegroundsLifecycleDetector _lifecycle;
    private readonly Queue<GameEvent> _pendingEvents = [];
    private readonly int _pendingEventCapacity;
    private IAppliedMatchEventObserver? _appliedEventObserver;
    private TrackingSnapshot _lastPublishedSnapshot;
    private TrackingSessionState _trackingState;
    private long _rawLinesReceived;
    private long _parsedEvents;
    private long _ignored;
    private long _unknown;
    private long _malformed;
    private long _trackingEventsApplied;
    private long _bufferedEventsDropped;
    private long _safetyLimitRejections;
    private DateTimeOffset? _lastStateUpdateTimestamp;

    public LiveTrackingCoordinator(
        PowerLineParser? parser = null,
        TrackingSession? tracking = null,
        BattlegroundsLifecycleDetector? lifecycle = null,
        int pendingEventCapacity = DefaultPendingEventCapacity,
        IAppliedMatchEventObserver? appliedEventObserver = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingEventCapacity);
        if (pendingEventCapacity > MaximumPendingEventCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pendingEventCapacity),
                $"Pending event capacity cannot exceed {MaximumPendingEventCapacity} events.");
        }

        _parser = parser ?? new PowerLineParser();
        _tracking = tracking ?? new TrackingSession();
        _lifecycle = lifecycle ?? new BattlegroundsLifecycleDetector();
        _pendingEventCapacity = pendingEventCapacity;
        _appliedEventObserver = appliedEventObserver;
        _lastPublishedSnapshot = _tracking.Current;
        _trackingState = _lastPublishedSnapshot.SessionState;
    }

    public TrackingSnapshot CurrentSnapshot => _tracking.Current;

    /// <summary>True after a throwing observer was permanently detached.</summary>
    public bool AppliedEventObserverDetached { get; private set; }

    public LiveTrackingDiagnostics Diagnostics => CreateDiagnostics();

    public LiveTrackingUpdate Process(RawLogLine rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        Increment(ref _rawLinesReceived);

        var parseResult = _parser.Parse(rawLine.Content, rawLine.Timestamp);
        CountParseResult(parseResult.Status);
        if (parseResult.Status != PowerParseStatus.Parsed || parseResult.Event is null)
        {
            return CreateUpdate(rawLine, parseResult, stateChanged: false, snapshot: null);
        }

        var gameEvent = parseResult.Event;
        var lifecycle = _lifecycle.Observe(gameEvent);
        if (lifecycle.Action == BattlegroundsLifecycleAction.GameBoundary)
        {
            _pendingEvents.Clear();
            var ended = EndActiveMatch(gameEvent.Timestamp);
            return CreateUpdate(rawLine, parseResult, ended is not null, ended);
        }

        if (lifecycle.Action == BattlegroundsLifecycleAction.EndMatch)
        {
            _pendingEvents.Clear();
            var ended = EndActiveMatch(gameEvent.Timestamp);
            return CreateUpdate(rawLine, parseResult, ended is not null, ended);
        }

        if (_trackingState != TrackingSessionState.Active)
        {
            Buffer(gameEvent);
            if (lifecycle.Action != BattlegroundsLifecycleAction.StartBattlegrounds)
            {
                return CreateUpdate(rawLine, parseResult, stateChanged: false, snapshot: null);
            }

            var started = StartAndReplayBuffered(lifecycle);
            return CreateUpdate(rawLine, parseResult, stateChanged: true, started);
        }

        var trackingUpdate = TryApplyToTracking(gameEvent);
        if (trackingUpdate is null)
        {
            return CreateUpdate(rawLine, parseResult, stateChanged: false, snapshot: null);
        }

        var terminalSnapshot = EndIfReducerReachedGameOver(gameEvent.Timestamp, trackingUpdate);
        if (terminalSnapshot is not null)
        {
            return CreateUpdate(rawLine, parseResult, stateChanged: true, terminalSnapshot);
        }

        var materiallyChanged = trackingUpdate.ObservedBoard is not null ||
                                !Equals(
                                    _lastPublishedSnapshot.Battlegrounds,
                                    trackingUpdate.Battlegrounds);
        var snapshot = materiallyChanged ? Publish(gameEvent.Timestamp) : null;
        return CreateUpdate(rawLine, parseResult, materiallyChanged, snapshot);
    }

    public async Task RunAsync(
        ChannelReader<RawLogLine> lines,
        Action<LiveTrackingUpdate>? onProcessed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        try
        {
            await foreach (var line in lines.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                onProcessed?.Invoke(Process(line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Reset()
    {
        _pendingEvents.Clear();
        _lifecycle.Reset();
        _tracking.Reset();
        _lastPublishedSnapshot = _tracking.Current;
        _trackingState = TrackingSessionState.Inactive;
        _lastStateUpdateTimestamp = null;
    }

    private TrackingSnapshot StartAndReplayBuffered(
        BattlegroundsLifecycleObservation lifecycle)
    {
        // The confirmation timestamp is the evidence-backed match time; the
        // candidate boundary can be a stale catch-up CREATE_GAME minutes old.
        var startedAt = lifecycle.ConfirmedAt ?? lifecycle.MatchStartedAt;
        _tracking.Reset();
        _ = _tracking.StartBattlegroundsMatch(
            startedAt,
            lifecycle.LocalPlayerId);
        _trackingState = TrackingSessionState.Active;
        NotifyMatchStarted(startedAt, lifecycle.LocalPlayerId);
        var lastTimestamp = startedAt;

        while (_pendingEvents.TryDequeue(out var pending) &&
               _trackingState == TrackingSessionState.Active)
        {
            lastTimestamp = pending.Timestamp;
            var update = TryApplyToTracking(pending);
            if (update is null)
            {
                continue;
            }

            var terminal = EndIfReducerReachedGameOver(pending.Timestamp, update);
            if (terminal is not null)
            {
                _pendingEvents.Clear();
                return terminal;
            }
        }

        _pendingEvents.Clear();
        return Publish(lastTimestamp);
    }

    private TrackingUpdate? TryApplyToTracking(GameEvent gameEvent)
    {
        try
        {
            var update = _tracking.Apply(gameEvent);
            Increment(ref _trackingEventsApplied);
            NotifyEventApplied(gameEvent);
            return update;
        }
        catch (TrackingSafetyLimitExceededException)
        {
            Increment(ref _safetyLimitRejections);
            NotifyEventRejected(gameEvent);
            return null;
        }
    }

    private TrackingSnapshot? EndIfReducerReachedGameOver(
        DateTimeOffset timestamp,
        TrackingUpdate update)
    {
        if (update.SessionState != TrackingSessionState.Active ||
            update.Battlegrounds.IsActive)
        {
            return null;
        }

        return EndActiveMatch(timestamp);
    }

    private TrackingSnapshot? EndActiveMatch(DateTimeOffset timestamp)
    {
        if (_trackingState != TrackingSessionState.Active)
        {
            return null;
        }

        _ = _tracking.EndMatch(timestamp);
        _trackingState = TrackingSessionState.Ended;
        NotifyMatchEnded(timestamp);
        return Publish(timestamp);
    }

    private TrackingSnapshot Publish(DateTimeOffset timestamp)
    {
        _lastStateUpdateTimestamp = timestamp;
        _lastPublishedSnapshot = _tracking.Current;
        return _lastPublishedSnapshot;
    }

    private void Buffer(GameEvent gameEvent)
    {
        if (gameEvent is GameCreated)
        {
            return;
        }

        while (_pendingEvents.Count >= _pendingEventCapacity)
        {
            _pendingEvents.Dequeue();
            Increment(ref _bufferedEventsDropped);
        }

        _pendingEvents.Enqueue(gameEvent);
    }

    private void CountParseResult(PowerParseStatus status)
    {
        switch (status)
        {
            case PowerParseStatus.Parsed:
                Increment(ref _parsedEvents);
                break;
            case PowerParseStatus.Ignored:
                Increment(ref _ignored);
                break;
            case PowerParseStatus.Malformed:
                Increment(ref _malformed);
                break;
            case PowerParseStatus.Unknown:
                Increment(ref _unknown);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private LiveTrackingUpdate CreateUpdate(
        RawLogLine rawLine,
        PowerParseResult parseResult,
        bool stateChanged,
        TrackingSnapshot? snapshot) => new(
            rawLine,
            parseResult,
            stateChanged,
            snapshot,
            CreateDiagnostics());

    private LiveTrackingDiagnostics CreateDiagnostics()
    {
        var snapshot = _lastPublishedSnapshot;
        var battlegrounds = snapshot.Battlegrounds;
        var limits = _tracking.Limits;
        var warnings = LiveTrackingWarnings.None;
        if (_tracking.EntityCount >= limits.TrackedEntityWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.TrackedEntities;
        }

        if (_tracking.MaximumTagsOnEntity >= limits.TagsPerEntityWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.TagsPerEntity;
        }

        if (_tracking.TagCount >= limits.TotalTagWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.TotalTags;
        }

        if (battlegrounds.Lobby.Count >= limits.LobbyPlayerWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.LobbyPlayers;
        }

        if (_tracking.MaximumTimelineEventsOnPlayer >= limits.TimelineEventWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.TimelineEvents;
        }

        if (_tracking.MaximumOpponentSnapshotsOnPlayer >= limits.OpponentSnapshotWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.OpponentSnapshots;
        }

        if (_lifecycle.PlayerIdentityCount >= _lifecycle.PlayerIdentityWarningThreshold)
        {
            warnings |= LiveTrackingWarnings.LifecyclePlayerIdentities;
        }

        if (AppliedEventObserverDetached)
        {
            warnings |= LiveTrackingWarnings.CaptureObserverDetached;
        }

        return new LiveTrackingDiagnostics(
            _rawLinesReceived,
            _parsedEvents,
            _ignored,
            _unknown,
            _malformed,
            _trackingEventsApplied,
            _bufferedEventsDropped,
            _safetyLimitRejections,
            _lifecycle.PlayerIdentityEvictions,
            snapshot.SessionState,
            battlegrounds.IsActive,
            battlegrounds.Turn,
            battlegrounds.Phase,
            battlegrounds.Lobby.Count,
            _tracking.EntityCount,
            _tracking.TagCount,
            _tracking.MaximumTagsOnEntity,
            _tracking.TimelineEventCount,
            _tracking.MaximumTimelineEventsOnPlayer,
            _tracking.OpponentSnapshotCount,
            _tracking.MaximumOpponentSnapshotsOnPlayer,
            warnings,
            battlegrounds.CurrentOpponentPlayerId,
            _lastStateUpdateTimestamp);
    }

    private void NotifyMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
    {
        if (_appliedEventObserver is not { } observer)
        {
            return;
        }

        try
        {
            observer.OnMatchStarted(timestamp, localPlayerId);
        }
        catch
        {
            DetachObserver();
        }
    }

    private void NotifyEventApplied(GameEvent gameEvent)
    {
        if (_appliedEventObserver is not { } observer)
        {
            return;
        }

        try
        {
            observer.OnEventApplied(gameEvent);
        }
        catch
        {
            DetachObserver();
        }
    }

    private void NotifyEventRejected(GameEvent gameEvent)
    {
        if (_appliedEventObserver is not { } observer)
        {
            return;
        }

        try
        {
            observer.OnEventRejected(gameEvent);
        }
        catch
        {
            DetachObserver();
        }
    }

    private void NotifyMatchEnded(DateTimeOffset timestamp)
    {
        if (_appliedEventObserver is not { } observer)
        {
            return;
        }

        try
        {
            observer.OnMatchEnded(timestamp);
        }
        catch
        {
            DetachObserver();
        }
    }

    private void DetachObserver()
    {
        _appliedEventObserver = null;
        AppliedEventObserverDetached = true;
    }

    private static void Increment(ref long counter)
    {
        if (counter < long.MaxValue)
        {
            counter++;
        }
    }
}
