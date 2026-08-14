using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds.Memory;

public sealed class OpponentMemoryService
{
    private readonly int _maximumOpponents;
    private readonly int _maximumSnapshotsPerOpponent;

    public OpponentMemoryService(
        int maximumOpponents = 16,
        int maximumSnapshotsPerOpponent = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOpponents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotsPerOpponent);
        _maximumOpponents = maximumOpponents;
        _maximumSnapshotsPerOpponent = maximumSnapshotsPerOpponent;
    }

    public OpponentMemory Memory { get; private set; } = OpponentMemory.Empty;

    public int SnapshotCount => Memory.Histories.Values.Sum(static history => history.Snapshots.Count);

    public int MaximumSnapshotCount => Memory.Histories.Count == 0
        ? 0
        : Memory.Histories.Values.Max(static history => history.Snapshots.Count);

    public BoardSnapshot Capture(
        int opponentPlayerId,
        int turn,
        IEnumerable<EntitySnapshot> entities,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var captured = BoardSnapshot.Capture(
            opponentPlayerId,
            turn,
            timestamp,
            entities);
        Memory = Memory.Remember(
            captured,
            _maximumOpponents,
            _maximumSnapshotsPerOpponent);
        return captured;
    }

    public void Reset() => Memory = OpponentMemory.Empty;
}
