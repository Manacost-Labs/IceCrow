using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording;

public sealed class MatchRecorder
{
    private readonly List<RecordedEvent> _events = [];
    private readonly List<ReplayCheckpoint> _checkpoints = [];
    private long _retainedBytes;
    private RecordingLifecycle _lifecycle;

    public MatchRecorder(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTimeOffset StartedAt { get; }

    public int EventCount => _events.Count;

    public void RecordMatchStarted(
        DateTimeOffset timestamp,
        int? localPlayerId = null)
    {
        if (_lifecycle != RecordingLifecycle.NotStarted)
        {
            throw new InvalidOperationException(
                "A recording can contain only one match start as its first event.");
        }

        Add(RecordedEvent.CreateMatchStarted(timestamp, localPlayerId));
        _lifecycle = RecordingLifecycle.Active;
    }

    public void Record(GameEvent gameEvent)
    {
        if (_lifecycle != RecordingLifecycle.Active)
        {
            throw new InvalidOperationException(
                "Normalized events can be recorded only inside an active match.");
        }

        Add(RecordedEvent.FromGameEvent(gameEvent));
    }

    public void RecordMatchEnded(DateTimeOffset timestamp)
    {
        if (_lifecycle != RecordingLifecycle.Active)
        {
            throw new InvalidOperationException(
                "A match can end only once after it has started.");
        }

        Add(RecordedEvent.CreateMatchEnded(timestamp));
        _lifecycle = RecordingLifecycle.Ended;
    }

    public ReplayCheckpoint AddCheckpoint(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_events.Count == 0)
        {
            throw new InvalidOperationException(
                "A checkpoint requires at least one recorded event.");
        }

        if (_checkpoints.Any(checkpoint =>
                string.Equals(checkpoint.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"A checkpoint named '{name}' already exists.");
        }

        if (_checkpoints.Count >= RecordingSerializer.MaximumCheckpointCount)
        {
            throw new InvalidOperationException(
                $"A recording cannot exceed {RecordingSerializer.MaximumCheckpointCount} checkpoints.");
        }

        var checkpoint = new ReplayCheckpoint(name, _events.Count - 1);
        RecordingSerializer.ValidateCheckpoint(checkpoint, _events.Count);
        ReserveRetainedBytes(RecordingSerializer.EstimateRetainedBytes(checkpoint));
        _checkpoints.Add(checkpoint);
        return checkpoint;
    }

    public RecordedMatch CreateMatch()
    {
        var match = new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            StartedAt,
            _events,
            _checkpoints);
        RecordingSerializer.Validate(match);
        return match;
    }

    private void Add(RecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);
        if (_events.Count >= RecordingSerializer.MaximumEventCount)
        {
            throw new InvalidOperationException(
                $"A recording cannot exceed {RecordingSerializer.MaximumEventCount} events.");
        }

        RecordingSerializer.ValidateEvent(recordedEvent);
        ReserveRetainedBytes(RecordingSerializer.EstimateRetainedBytes(recordedEvent));
        _events.Add(recordedEvent);
    }

    private void ReserveRetainedBytes(long byteCount)
    {
        if (_retainedBytes > RecordingSerializer.MaximumRetainedBytes - byteCount)
        {
            throw new InvalidOperationException(
                $"A recording cannot retain more than {RecordingSerializer.MaximumRetainedBytes} estimated bytes.");
        }

        _retainedBytes += byteCount;
    }

    private enum RecordingLifecycle
    {
        NotStarted,
        Active,
        Ended,
    }
}
