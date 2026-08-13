using System.Collections.ObjectModel;

namespace IceCrow.Recording;

public sealed class RecordedMatch
{
    public const int CurrentFormatVersion = 1;

    private readonly RecordedEvent[] _events;
    private readonly ReplayCheckpoint[] _checkpoints;
    private readonly ReadOnlyCollection<RecordedEvent> _readOnlyEvents;
    private readonly ReadOnlyCollection<ReplayCheckpoint> _readOnlyCheckpoints;

    public RecordedMatch(
        int formatVersion,
        DateTimeOffset startedAt,
        IEnumerable<RecordedEvent> events,
        IEnumerable<ReplayCheckpoint>? checkpoints = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        FormatVersion = formatVersion;
        StartedAt = startedAt;
        _events = MaterializeBounded(
            events,
            RecordingSerializer.MaximumEventCount,
            "events");
        _checkpoints = checkpoints is null
            ? []
            : MaterializeBounded(
                checkpoints,
                RecordingSerializer.MaximumCheckpointCount,
                "checkpoints");
        _readOnlyEvents = Array.AsReadOnly(_events);
        _readOnlyCheckpoints = Array.AsReadOnly(_checkpoints);
    }

    public int FormatVersion { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyList<RecordedEvent> Events => _readOnlyEvents;

    public IReadOnlyList<ReplayCheckpoint> Checkpoints => _readOnlyCheckpoints;

    private static T[] MaterializeBounded<T>(
        IEnumerable<T> source,
        int maximumCount,
        string collectionName)
    {
        var values = source.Take(maximumCount + 1).ToArray();
        if (values.Length > maximumCount)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {maximumCount} {collectionName} limit.");
        }

        return values;
    }
}
